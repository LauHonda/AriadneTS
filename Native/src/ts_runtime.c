#include "ariadnets/ts_runtime.h"

#include <stdlib.h>
#include <string.h>
#include <time.h>

#if defined(_WIN32)
#include <windows.h>
#endif

#include "quickjs.h"

#define TS_RUNTIME_ABI_VERSION 3u
#define TS_DEFAULT_FILENAME "<eval>"

struct ts_runtime {
    JSRuntime* js_runtime;
    JSContext* js_context;
    ts_log_callback log_callback;
    void* log_user_data;
    ts_module_load_callback module_load_callback;
    void* module_load_user_data;
    char* last_error;
    size_t last_error_length;
    char* last_result;
    size_t last_result_length;
    char* unhandled_rejection;
    size_t unhandled_rejection_length;
    uint64_t execution_timeout_nanoseconds;
    uint64_t deadline_nanoseconds;
};

static ts_status eval_source(
    ts_runtime* runtime,
    const char* source,
    size_t source_length,
    const char* filename,
    int flags
);

static uint64_t monotonic_nanoseconds(void) {
#if defined(_WIN32)
    LARGE_INTEGER counter;
    LARGE_INTEGER frequency;
    QueryPerformanceCounter(&counter);
    QueryPerformanceFrequency(&frequency);
    uint64_t seconds = (uint64_t)(counter.QuadPart / frequency.QuadPart);
    uint64_t remainder = (uint64_t)(counter.QuadPart % frequency.QuadPart);
    return seconds * 1000000000ull +
        (remainder * 1000000000ull) / (uint64_t)frequency.QuadPart;
#else
    struct timespec now;
    clock_gettime(CLOCK_MONOTONIC, &now);
    return (uint64_t)now.tv_sec * 1000000000ull + (uint64_t)now.tv_nsec;
#endif
}

static void begin_operation(ts_runtime* runtime) {
    runtime->deadline_nanoseconds = runtime->execution_timeout_nanoseconds == 0
        ? 0
        : monotonic_nanoseconds() + runtime->execution_timeout_nanoseconds;
}

static void end_operation(ts_runtime* runtime) {
    runtime->deadline_nanoseconds = 0;
}

static int interrupt_handler(JSRuntime* js_runtime, void* opaque) {
    (void)js_runtime;

    ts_runtime* runtime = opaque;
    return runtime != NULL &&
        runtime->deadline_nanoseconds != 0 &&
        monotonic_nanoseconds() >= runtime->deadline_nanoseconds;
}

static void clear_last_error(ts_runtime* runtime) {
    free(runtime->last_error);
    runtime->last_error = NULL;
    runtime->last_error_length = 0;
}

static void clear_last_result(ts_runtime* runtime) {
    free(runtime->last_result);
    runtime->last_result = NULL;
    runtime->last_result_length = 0;
}

static void clear_unhandled_rejection(ts_runtime* runtime) {
    free(runtime->unhandled_rejection);
    runtime->unhandled_rejection = NULL;
    runtime->unhandled_rejection_length = 0;
}

static void set_last_error(ts_runtime* runtime, const char* message, size_t length) {
    clear_last_error(runtime);
    clear_last_result(runtime);

    runtime->last_error = malloc(length + 1);
    if (runtime->last_error == NULL) {
        return;
    }

    memcpy(runtime->last_error, message, length);
    runtime->last_error[length] = '\0';
    runtime->last_error_length = length;
}

static void set_exception_error(ts_runtime* runtime) {
    JSValue exception = JS_GetException(runtime->js_context);
    JSValue stack = JS_GetPropertyStr(runtime->js_context, exception, "stack");
    const char* message = JS_ToCString(runtime->js_context, exception);
    const char* stack_message = JS_IsUndefined(stack)
        ? NULL
        : JS_ToCString(runtime->js_context, stack);

    if (message != NULL && stack_message != NULL) {
        size_t message_length = strlen(message);
        size_t stack_length = strlen(stack_message);
        char* combined = malloc(message_length + stack_length + 2);

        if (combined != NULL) {
            memcpy(combined, message, message_length);
            combined[message_length] = '\n';
            memcpy(combined + message_length + 1, stack_message, stack_length);
            combined[message_length + stack_length + 1] = '\0';
            set_last_error(runtime, combined, message_length + stack_length + 1);
            free(combined);
        } else {
            set_last_error(runtime, message, message_length);
        }
    } else if (message != NULL) {
        set_last_error(runtime, message, strlen(message));
    } else if (stack_message != NULL) {
        set_last_error(runtime, stack_message, strlen(stack_message));
    } else {
        static const char fallback[] = "JavaScript exception";
        set_last_error(runtime, fallback, sizeof(fallback) - 1);
    }

    if (stack_message != NULL) {
        JS_FreeCString(runtime->js_context, stack_message);
    }
    if (message != NULL) {
        JS_FreeCString(runtime->js_context, message);
    }
    JS_FreeValue(runtime->js_context, stack);
    JS_FreeValue(runtime->js_context, exception);
}

static JSValue host_log(
    JSContext* context,
    JSValueConst this_value,
    int argument_count,
    JSValueConst* arguments
) {
    (void)this_value;

    ts_runtime* runtime = JS_GetContextOpaque(context);
    if (runtime == NULL || runtime->log_callback == NULL) {
        return JS_UNDEFINED;
    }

    for (int index = 0; index < argument_count; ++index) {
        const char* message = JS_ToCString(context, arguments[index]);
        if (message == NULL) {
            return JS_EXCEPTION;
        }

        runtime->log_callback(runtime->log_user_data, message, strlen(message));
        JS_FreeCString(context, message);
    }

    return JS_UNDEFINED;
}

static void track_promise_rejection(
    JSContext* context,
    JSValueConst promise,
    JSValueConst reason,
    JS_BOOL is_handled,
    void* opaque
) {
    (void)promise;

    ts_runtime* runtime = opaque;
    if (runtime == NULL) {
        return;
    }
    if (is_handled) {
        clear_unhandled_rejection(runtime);
        return;
    }

    const char* message = JS_ToCString(context, reason);
    if (message == NULL) {
        return;
    }

    size_t length = strlen(message);
    char* copy = malloc(length + 1);
    if (copy != NULL) {
        memcpy(copy, message, length + 1);
        clear_unhandled_rejection(runtime);
        runtime->unhandled_rejection = copy;
        runtime->unhandled_rejection_length = length;
    }
    JS_FreeCString(context, message);
}

static JSModuleDef* load_module(
    JSContext* context,
    const char* module_name,
    void* opaque
) {
    ts_runtime* runtime = opaque;
    if (runtime == NULL || runtime->module_load_callback == NULL) {
        JS_ThrowReferenceError(context, "no host module loader for '%s'", module_name);
        return NULL;
    }

    size_t source_length = 0;
    ts_status status = runtime->module_load_callback(
        runtime->module_load_user_data,
        module_name,
        strlen(module_name),
        NULL,
        0,
        &source_length
    );
    if (status != TS_STATUS_OK) {
        JS_ThrowReferenceError(context, "could not load module '%s' (status %d)", module_name, status);
        return NULL;
    }

    char* source = malloc(source_length + 1);
    if (source == NULL) {
        JS_ThrowOutOfMemory(context);
        return NULL;
    }

    size_t written_length = source_length;
    status = runtime->module_load_callback(
        runtime->module_load_user_data,
        module_name,
        strlen(module_name),
        source,
        source_length,
        &written_length
    );
    if (status != TS_STATUS_OK || written_length > source_length) {
        free(source);
        JS_ThrowReferenceError(context, "could not read module '%s' (status %d)", module_name, status);
        return NULL;
    }
    source[written_length] = '\0';

    JSValue compiled = JS_Eval(
        context,
        source,
        written_length,
        module_name,
        JS_EVAL_TYPE_MODULE | JS_EVAL_FLAG_COMPILE_ONLY
    );
    free(source);

    if (JS_IsException(compiled)) {
        return NULL;
    }

    JSModuleDef* module = JS_VALUE_GET_PTR(compiled);
    JS_FreeValue(context, compiled);
    return module;
}

static int install_host_api(ts_runtime* runtime) {
    JSContext* context = runtime->js_context;
    JSValue global = JS_GetGlobalObject(context);
    JSValue host = JS_NewObject(context);
    JSValue log = JS_NewCFunction(context, host_log, "log", 1);

    if (JS_IsException(global) || JS_IsException(host) || JS_IsException(log)) {
        JS_FreeValue(context, log);
        JS_FreeValue(context, host);
        JS_FreeValue(context, global);
        return 0;
    }

    JS_SetPropertyStr(context, host, "log", log);
    JS_SetPropertyStr(context, global, "host", host);
    JS_FreeValue(context, global);
    return 1;
}

uint32_t ts_runtime_abi_version(void) {
    return TS_RUNTIME_ABI_VERSION;
}

ts_runtime* ts_runtime_create(const ts_runtime_config* config) {
    if (config == NULL || config->struct_size < sizeof(ts_runtime_config)) {
        return NULL;
    }

    ts_runtime* runtime = calloc(1, sizeof(ts_runtime));
    if (runtime == NULL) {
        return NULL;
    }

    runtime->log_callback = config->log_callback;
    runtime->log_user_data = config->log_user_data;
    runtime->module_load_callback = config->module_load_callback;
    runtime->module_load_user_data = config->module_load_user_data;
    runtime->execution_timeout_nanoseconds =
        (uint64_t)config->execution_timeout_milliseconds * 1000000ull;
    runtime->js_runtime = JS_NewRuntime();
    if (runtime->js_runtime == NULL) {
        ts_runtime_destroy(runtime);
        return NULL;
    }

    if (config->memory_limit_bytes > 0) {
        JS_SetMemoryLimit(runtime->js_runtime, (size_t)config->memory_limit_bytes);
    }
    if (config->max_stack_size_bytes > 0) {
        JS_SetMaxStackSize(runtime->js_runtime, (size_t)config->max_stack_size_bytes);
    }

    JS_SetModuleLoaderFunc(runtime->js_runtime, NULL, load_module, runtime);
    JS_SetInterruptHandler(runtime->js_runtime, interrupt_handler, runtime);
    JS_SetHostPromiseRejectionTracker(
        runtime->js_runtime,
        track_promise_rejection,
        runtime
    );

    runtime->js_context = JS_NewContext(runtime->js_runtime);
    if (runtime->js_context == NULL) {
        ts_runtime_destroy(runtime);
        return NULL;
    }

    JS_SetContextOpaque(runtime->js_context, runtime);
    if (!install_host_api(runtime)) {
        ts_runtime_destroy(runtime);
        return NULL;
    }

    return runtime;
}

void ts_runtime_destroy(ts_runtime* runtime) {
    if (runtime == NULL) {
        return;
    }

    clear_last_error(runtime);
    clear_last_result(runtime);
    clear_unhandled_rejection(runtime);
    if (runtime->js_context != NULL) {
        JS_FreeContext(runtime->js_context);
    }
    if (runtime->js_runtime != NULL) {
        JS_FreeRuntime(runtime->js_runtime);
    }
    free(runtime);
}

ts_status ts_runtime_eval(
    ts_runtime* runtime,
    const char* source,
    size_t source_length,
    const char* filename
) {
    return eval_source(
        runtime,
        source,
        source_length,
        filename != NULL ? filename : TS_DEFAULT_FILENAME,
        JS_EVAL_TYPE_GLOBAL
    );
}

ts_status ts_runtime_eval_module(
    ts_runtime* runtime,
    const char* source,
    size_t source_length,
    const char* module_name
) {
    if (module_name == NULL) {
        return TS_STATUS_INVALID_ARGUMENT;
    }

    return eval_source(runtime, source, source_length, module_name, JS_EVAL_TYPE_MODULE);
}

ts_status ts_runtime_execute_pending_jobs(
    ts_runtime* runtime,
    uint32_t max_jobs,
    uint32_t* executed_jobs
) {
    if (runtime == NULL) {
        return TS_STATUS_INVALID_ARGUMENT;
    }

    clear_last_error(runtime);
    begin_operation(runtime);
    uint32_t executed = 0;
    while (max_jobs == 0 || executed < max_jobs) {
        JSContext* job_context = NULL;
        int result = JS_ExecutePendingJob(runtime->js_runtime, &job_context);
        if (result == 0) {
            break;
        }
        if (result < 0) {
            set_exception_error(runtime);
            if (executed_jobs != NULL) {
                *executed_jobs = executed;
            }
            end_operation(runtime);
            return TS_STATUS_SCRIPT_ERROR;
        }
        ++executed;
    }

    if (executed_jobs != NULL) {
        *executed_jobs = executed;
    }
    if (!JS_IsJobPending(runtime->js_runtime) && runtime->unhandled_rejection != NULL) {
        set_last_error(
            runtime,
            runtime->unhandled_rejection,
            runtime->unhandled_rejection_length
        );
        clear_unhandled_rejection(runtime);
        end_operation(runtime);
        return TS_STATUS_SCRIPT_ERROR;
    }
    end_operation(runtime);
    return TS_STATUS_OK;
}

ts_status ts_runtime_invoke(
    ts_runtime* runtime,
    const char* method,
    size_t method_length,
    const char* payload_json,
    size_t payload_json_length
) {
    if (runtime == NULL || method == NULL || payload_json == NULL) {
        return TS_STATUS_INVALID_ARGUMENT;
    }

    clear_last_error(runtime);
    clear_last_result(runtime);
    begin_operation(runtime);

    JSContext* context = runtime->js_context;
    JSValue global = JS_GetGlobalObject(context);
    JSValue invoke = JS_GetPropertyStr(context, global, "__ariadnets_invoke");
    if (!JS_IsFunction(context, invoke)) {
        static const char message[] = "globalThis.__ariadnets_invoke is not a function";
        set_last_error(runtime, message, sizeof(message) - 1);
        JS_FreeValue(context, invoke);
        JS_FreeValue(context, global);
        end_operation(runtime);
        return TS_STATUS_SCRIPT_ERROR;
    }

    JSValue arguments[2];
    arguments[0] = JS_NewStringLen(context, method, method_length);
    arguments[1] = JS_ParseJSON(context, payload_json, payload_json_length, "<invoke-payload>");
    if (JS_IsException(arguments[0]) || JS_IsException(arguments[1])) {
        JS_FreeValue(context, arguments[1]);
        JS_FreeValue(context, arguments[0]);
        JS_FreeValue(context, invoke);
        JS_FreeValue(context, global);
        set_exception_error(runtime);
        end_operation(runtime);
        return TS_STATUS_SCRIPT_ERROR;
    }

    JSValue result = JS_Call(context, invoke, global, 2, arguments);
    JS_FreeValue(context, arguments[1]);
    JS_FreeValue(context, arguments[0]);
    JS_FreeValue(context, invoke);
    JS_FreeValue(context, global);
    if (JS_IsException(result)) {
        JS_FreeValue(context, result);
        set_exception_error(runtime);
        end_operation(runtime);
        return TS_STATUS_SCRIPT_ERROR;
    }

    JSValue json = JS_JSONStringify(context, result, JS_UNDEFINED, JS_UNDEFINED);
    JS_FreeValue(context, result);
    if (JS_IsException(json)) {
        JS_FreeValue(context, json);
        set_exception_error(runtime);
        end_operation(runtime);
        return TS_STATUS_SCRIPT_ERROR;
    }

    if (!JS_IsUndefined(json)) {
        size_t result_length = 0;
        const char* result_json = JS_ToCStringLen(context, &result_length, json);
        if (result_json == NULL) {
            JS_FreeValue(context, json);
            set_exception_error(runtime);
            end_operation(runtime);
            return TS_STATUS_SCRIPT_ERROR;
        }

        runtime->last_result = malloc(result_length + 1);
        if (runtime->last_result == NULL) {
            JS_FreeCString(context, result_json);
            JS_FreeValue(context, json);
            end_operation(runtime);
            return TS_STATUS_OUT_OF_MEMORY;
        }
        memcpy(runtime->last_result, result_json, result_length);
        runtime->last_result[result_length] = '\0';
        runtime->last_result_length = result_length;
        JS_FreeCString(context, result_json);
    }

    JS_FreeValue(context, json);
    end_operation(runtime);
    return TS_STATUS_OK;
}

const char* ts_runtime_last_result(const ts_runtime* runtime, size_t* result_length) {
    if (result_length != NULL) {
        *result_length = runtime != NULL ? runtime->last_result_length : 0;
    }
    return runtime != NULL ? runtime->last_result : NULL;
}

static ts_status eval_source(
    ts_runtime* runtime,
    const char* source,
    size_t source_length,
    const char* filename,
    int flags
) {
    if (runtime == NULL || source == NULL) {
        return TS_STATUS_INVALID_ARGUMENT;
    }

    clear_last_error(runtime);
    clear_last_result(runtime);
    begin_operation(runtime);
    JSValue result = JS_Eval(
        runtime->js_context,
        source,
        source_length,
        filename,
        flags
    );

    if (JS_IsException(result)) {
        JS_FreeValue(runtime->js_context, result);
        set_exception_error(runtime);
        end_operation(runtime);
        return TS_STATUS_SCRIPT_ERROR;
    }

    JS_FreeValue(runtime->js_context, result);
    end_operation(runtime);
    return TS_STATUS_OK;
}

const char* ts_runtime_last_error(const ts_runtime* runtime, size_t* error_length) {
    if (error_length != NULL) {
        *error_length = runtime != NULL ? runtime->last_error_length : 0;
    }
    return runtime != NULL ? runtime->last_error : NULL;
}
