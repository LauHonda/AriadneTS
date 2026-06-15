#include "ariadnets/ts_runtime.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int log_count = 0;

typedef struct test_module {
    const char* name;
    const char* source;
} test_module;

static const test_module modules[] = {
    { "math.js", "export const answer = 42;" },
};

static void on_log(void* user_data, const char* message, size_t message_length) {
    const char* prefix = user_data;
    printf("%s%.*s\n", prefix, (int)message_length, message);
    ++log_count;
}

static ts_status on_load_module(
    void* user_data,
    const char* module_name,
    size_t module_name_length,
    char* source_buffer,
    size_t source_capacity,
    size_t* source_length
) {
    (void)user_data;

    for (size_t index = 0; index < sizeof(modules) / sizeof(modules[0]); ++index) {
        const test_module* module = &modules[index];
        size_t name_length = strlen(module->name);
        if (name_length != module_name_length ||
            memcmp(module->name, module_name, module_name_length) != 0) {
            continue;
        }

        size_t length = strlen(module->source);
        *source_length = length;
        if (source_buffer == NULL) {
            return TS_STATUS_OK;
        }
        if (source_capacity < length) {
            return TS_STATUS_BUFFER_TOO_SMALL;
        }

        memcpy(source_buffer, module->source, length);
        return TS_STATUS_OK;
    }

    return TS_STATUS_MODULE_NOT_FOUND;
}

static ts_status on_host_invoke(
    void* user_data,
    const char* method,
    size_t method_length,
    const char* payload_json,
    size_t payload_json_length,
    char* result_buffer,
    size_t result_capacity,
    size_t* result_length
) {
    (void)user_data;

    static const char expected_method[] = "math.double";
    static const char expected_payload[] = "{\"value\":21}";
    static const char result[] = "{\"value\":42}";
    if (method_length != sizeof(expected_method) - 1 ||
        memcmp(method, expected_method, method_length) != 0 ||
        payload_json_length != sizeof(expected_payload) - 1 ||
        memcmp(payload_json, expected_payload, payload_json_length) != 0) {
        return TS_STATUS_HOST_ERROR;
    }

    *result_length = sizeof(result) - 1;
    if (result_buffer == NULL) {
        return TS_STATUS_OK;
    }
    if (result_capacity < sizeof(result) - 1) {
        return TS_STATUS_BUFFER_TOO_SMALL;
    }

    memcpy(result_buffer, result, sizeof(result) - 1);
    return TS_STATUS_OK;
}

static void require_status(ts_runtime* runtime, ts_status actual, ts_status expected) {
    if (actual == expected) {
        return;
    }

    size_t error_length = 0;
    const char* error = ts_runtime_last_error(runtime, &error_length);
    fprintf(
        stderr,
        "expected status %d, got %d: %.*s\n",
        expected,
        actual,
        (int)error_length,
        error != NULL ? error : ""
    );
    exit(1);
}

int main(void) {
    ts_runtime_config config = {
        .struct_size = sizeof(ts_runtime_config),
        .memory_limit_bytes = 64 * 1024 * 1024,
        .log_callback = on_log,
        .log_user_data = "[js] ",
        .module_load_callback = on_load_module,
        .module_load_user_data = NULL,
        .max_stack_size_bytes = 1024 * 1024,
        .execution_timeout_milliseconds = 100,
        .host_invoke_callback = on_host_invoke,
        .host_invoke_user_data = NULL
    };

    ts_runtime* runtime = ts_runtime_create(&config);
    if (runtime == NULL) {
        fprintf(stderr, "failed to create runtime\n");
        return 1;
    }

    const char* hello_script = "host.log('hello from QuickJS');";
    require_status(
        runtime,
        ts_runtime_eval(runtime, hello_script, strlen(hello_script), "hello.js"),
        TS_STATUS_OK
    );

    const char* error_script = "throw new Error('expected failure');";
    require_status(
        runtime,
        ts_runtime_eval(runtime, error_script, strlen(error_script), "error.js"),
        TS_STATUS_SCRIPT_ERROR
    );

    size_t error_length = 0;
    const char* error = ts_runtime_last_error(runtime, &error_length);
    if (error == NULL || strstr(error, "expected failure") == NULL) {
        fprintf(stderr, "runtime did not preserve the expected script error\n");
        ts_runtime_destroy(runtime);
        return 1;
    }

    const char* module_script =
        "import { answer } from './math.js';"
        "host.log(`module answer: ${answer}`);"
        "Promise.resolve().then(() => host.log('promise job ran'));";
    require_status(
        runtime,
        ts_runtime_eval_module(runtime, module_script, strlen(module_script), "entry.js"),
        TS_STATUS_OK
    );

    uint32_t executed_jobs = 0;
    require_status(
        runtime,
        ts_runtime_execute_pending_jobs(runtime, 0, &executed_jobs),
        TS_STATUS_OK
    );
    if (executed_jobs == 0) {
        fprintf(stderr, "expected at least one pending promise job\n");
        ts_runtime_destroy(runtime);
        return 1;
    }

    const char* missing_module_script = "import './missing.js';";
    require_status(
        runtime,
        ts_runtime_eval_module(
            runtime,
            missing_module_script,
            strlen(missing_module_script),
            "missing-entry.js"
        ),
        TS_STATUS_SCRIPT_ERROR
    );
    error = ts_runtime_last_error(runtime, &error_length);
    if (error == NULL || strstr(error, "missing.js") == NULL) {
        fprintf(stderr, "missing module error did not preserve the module name\n");
        ts_runtime_destroy(runtime);
        return 1;
    }

    const char* invoke_script =
        "globalThis.__ariadnets_invoke = (method, payload) => {"
        "  if (method === 'sum') return payload.left + payload.right;"
        "  throw new Error(`unknown method: ${method}`);"
        "};";
    require_status(
        runtime,
        ts_runtime_eval_module(runtime, invoke_script, strlen(invoke_script), "invoke-entry.js"),
        TS_STATUS_OK
    );
    const char* invoke_payload = "{\"left\":19,\"right\":23}";
    require_status(
        runtime,
        ts_runtime_invoke(runtime, "sum", 3, invoke_payload, strlen(invoke_payload)),
        TS_STATUS_OK
    );
    size_t result_length = 0;
    const char* result = ts_runtime_last_result(runtime, &result_length);
    if (result == NULL || result_length != 2 || memcmp(result, "42", 2) != 0) {
        fprintf(stderr, "invoke result did not match expected JSON\n");
        ts_runtime_destroy(runtime);
        return 1;
    }

    const char* host_invoke_script =
        "globalThis.__ariadnets_invoke = () => "
        "host.invoke('math.double', { value: 21 }).value;";
    require_status(
        runtime,
        ts_runtime_eval_module(
            runtime,
            host_invoke_script,
            strlen(host_invoke_script),
            "host-invoke-entry.js"
        ),
        TS_STATUS_OK
    );
    require_status(
        runtime,
        ts_runtime_invoke(runtime, "hostInvoke", 10, "null", 4),
        TS_STATUS_OK
    );
    result = ts_runtime_last_result(runtime, &result_length);
    if (result == NULL || result_length != 2 || memcmp(result, "42", 2) != 0) {
        fprintf(stderr, "host invoke result did not match expected JSON\n");
        ts_runtime_destroy(runtime);
        return 1;
    }

    const char* rejection_script =
        "Promise.resolve().then(() => { throw new Error('expected rejection'); });";
    require_status(
        runtime,
        ts_runtime_eval(runtime, rejection_script, strlen(rejection_script), "rejection.js"),
        TS_STATUS_OK
    );
    require_status(
        runtime,
        ts_runtime_execute_pending_jobs(runtime, 0, &executed_jobs),
        TS_STATUS_SCRIPT_ERROR
    );
    error = ts_runtime_last_error(runtime, &error_length);
    if (error == NULL || strstr(error, "expected rejection") == NULL) {
        fprintf(stderr, "unhandled promise rejection was not reported\n");
        ts_runtime_destroy(runtime);
        return 1;
    }

    const char* infinite_loop_script = "while (true) {}";
    require_status(
        runtime,
        ts_runtime_eval(
            runtime,
            infinite_loop_script,
            strlen(infinite_loop_script),
            "infinite-loop.js"
        ),
        TS_STATUS_SCRIPT_ERROR
    );

    ts_runtime_destroy(runtime);

    if (log_count != 3) {
        fprintf(stderr, "expected three host log calls, got %d\n", log_count);
        return 1;
    }

    printf("native runtime smoke test passed\n");
    return 0;
}
