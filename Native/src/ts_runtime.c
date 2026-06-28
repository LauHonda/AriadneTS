#if !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#define _POSIX_C_SOURCE 200809L
#endif

#include "ariadnets/ts_runtime.h"

#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#if defined(_WIN32)
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
typedef SOCKET ts_socket;
#define TS_INVALID_SOCKET INVALID_SOCKET
#define ts_close_socket closesocket
#else
#include <arpa/inet.h>
#include <errno.h>
#include <netinet/in.h>
#include <pthread.h>
#include <sys/select.h>
#include <sys/socket.h>
#include <unistd.h>
typedef int ts_socket;
#define TS_INVALID_SOCKET (-1)
#define ts_close_socket close
#endif

#include "quickjs.h"

#define TS_RUNTIME_ABI_VERSION 5u
#define TS_DEFAULT_FILENAME "<eval>"
#define TS_MAX_DEBUG_BREAKPOINTS 256

typedef struct ts_debug_breakpoint {
    char* module;
    int32_t line;
} ts_debug_breakpoint;

typedef enum ts_debug_step_mode {
    TS_DEBUG_STEP_NONE = 0,
    TS_DEBUG_STEP_IN = 1,
    TS_DEBUG_STEP_OVER = 2,
    TS_DEBUG_STEP_OUT = 3
} ts_debug_step_mode;

struct ts_runtime {
    JSRuntime* js_runtime;
    JSContext* js_context;
    ts_log_callback log_callback;
    void* log_user_data;
    ts_module_load_callback module_load_callback;
    void* module_load_user_data;
    ts_host_invoke_callback host_invoke_callback;
    void* host_invoke_user_data;
    char* last_error;
    size_t last_error_length;
    char* last_result;
    size_t last_result_length;
    char* unhandled_rejection;
    size_t unhandled_rejection_length;
    uint64_t execution_timeout_nanoseconds;
    uint64_t deadline_nanoseconds;
    uint32_t debug_enabled;
    uint32_t debug_protocol;
    char* debug_host;
    uint16_t debug_port;
    uint32_t debug_wait_for_attach;
    volatile int debug_stop_requested;
    volatile int debug_client_attached;
    volatile uint32_t debug_continue_counter;
    volatile int debug_pause_active;
    volatile uint32_t debug_pause_sequence;
    volatile int debug_step_requested;
    volatile int debug_step_mode;
    int32_t debug_step_target_stack_depth;
    uint32_t debug_paused_id;
    char* debug_paused_module;
    char* debug_paused_function;
    char* debug_paused_variables_json;
    char* debug_paused_stack;
    int32_t debug_paused_stack_depth;
    int32_t debug_paused_line;
    int32_t debug_paused_column;
    ts_debug_breakpoint debug_breakpoints[TS_MAX_DEBUG_BREAKPOINTS];
    size_t debug_breakpoint_count;
    ts_socket debug_listen_socket;
#if defined(_WIN32)
    HANDLE debug_thread;
#else
    pthread_t debug_thread;
    int debug_thread_started;
#endif
};

static ts_status eval_source(
    ts_runtime* runtime,
    const char* source,
    size_t source_length,
    const char* filename,
    int flags
);

static int config_has_field(const ts_runtime_config* config, size_t field_end) {
    return config != NULL && config->struct_size >= field_end;
}

static char* duplicate_c_string(const char* text) {
    if (text == NULL) {
        text = "";
    }

    size_t length = strlen(text);
    char* copy = malloc(length + 1);
    if (copy == NULL) {
        return NULL;
    }
    memcpy(copy, text, length + 1);
    return copy;
}

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

static void sleep_milliseconds(uint32_t milliseconds) {
#if defined(_WIN32)
    Sleep(milliseconds);
#else
    struct timespec duration;
    duration.tv_sec = milliseconds / 1000u;
    duration.tv_nsec = (long)(milliseconds % 1000u) * 1000000L;
    while (nanosleep(&duration, &duration) != 0 && errno == EINTR) {
    }
#endif
}

static int interrupt_handler(JSRuntime* js_runtime, void* opaque) {
    (void)js_runtime;

    ts_runtime* runtime = opaque;
    return runtime != NULL &&
        runtime->deadline_nanoseconds != 0 &&
        monotonic_nanoseconds() >= runtime->deadline_nanoseconds;
}

static void debug_log(ts_runtime* runtime, const char* message) {
    if (runtime != NULL && runtime->log_callback != NULL && message != NULL) {
        runtime->log_callback(runtime->log_user_data, message, strlen(message));
    }
}

static void clear_debug_pause_location(ts_runtime* runtime) {
    if (runtime == NULL) {
        return;
    }

    runtime->debug_pause_active = 0;
    free(runtime->debug_paused_module);
    free(runtime->debug_paused_function);
    free(runtime->debug_paused_variables_json);
    free(runtime->debug_paused_stack);
    runtime->debug_paused_module = NULL;
    runtime->debug_paused_function = NULL;
    runtime->debug_paused_variables_json = NULL;
    runtime->debug_paused_stack = NULL;
    runtime->debug_paused_stack_depth = 0;
    runtime->debug_paused_id = 0;
    runtime->debug_paused_line = 0;
    runtime->debug_paused_column = 0;
}

static int debug_command_contains(const char* command, const char* needle) {
    return command != NULL && needle != NULL && strstr(command, needle) != NULL;
}

static int32_t debug_stack_depth(const char* stack) {
    if (stack == NULL || stack[0] == '\0') {
        return 0;
    }

    int32_t depth = 0;
    const char* line_start = stack;
    while (*line_start != '\0') {
        const char* line_end = strchr(line_start, '\n');
        size_t line_length = line_end != NULL
            ? (size_t)(line_end - line_start)
            : strlen(line_start);
        while (line_length > 0 &&
            (line_start[line_length - 1] == '\r' || line_start[line_length - 1] == ' ' || line_start[line_length - 1] == '\t')) {
            --line_length;
        }

        const char* cursor = line_start;
        while (line_length > 0 && (*cursor == ' ' || *cursor == '\t')) {
            ++cursor;
            --line_length;
        }
        if (line_length > 0 &&
            (strncmp(cursor, "at ", 3) == 0 || memchr(cursor, '@', line_length) != NULL)) {
            ++depth;
        }

        if (line_end == NULL) {
            break;
        }
        line_start = line_end + 1;
    }
    return depth;
}

static int debug_json_string_value(
    const char* json,
    const char* key,
    char* output,
    size_t output_capacity
) {
    if (json == NULL || key == NULL || output == NULL || output_capacity == 0) {
        return 0;
    }

    char pattern[64];
    snprintf(pattern, sizeof(pattern), "\"%s\"", key);
    const char* cursor = strstr(json, pattern);
    if (cursor == NULL) {
        return 0;
    }
    cursor += strlen(pattern);
    while (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n') {
        ++cursor;
    }
    if (*cursor != ':') {
        return 0;
    }
    ++cursor;
    while (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n') {
        ++cursor;
    }
    if (*cursor != '"') {
        return 0;
    }
    ++cursor;

    size_t length = 0;
    while (*cursor != '\0' && *cursor != '"' && length + 1 < output_capacity) {
        if (*cursor == '\\' && cursor[1] != '\0') {
            ++cursor;
        }
        output[length++] = *cursor++;
    }
    if (*cursor != '"') {
        return 0;
    }
    output[length] = '\0';
    return 1;
}

static int debug_json_int_value(const char* json, const char* key, int32_t* output) {
    if (json == NULL || key == NULL || output == NULL) {
        return 0;
    }

    char pattern[64];
    snprintf(pattern, sizeof(pattern), "\"%s\"", key);
    const char* cursor = strstr(json, pattern);
    if (cursor == NULL) {
        return 0;
    }
    cursor += strlen(pattern);
    while (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n') {
        ++cursor;
    }
    if (*cursor != ':') {
        return 0;
    }
    ++cursor;
    while (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n') {
        ++cursor;
    }

    char* parse_end = NULL;
    long value = strtol(cursor, &parse_end, 10);
    if (parse_end == cursor || value < INT32_MIN || value > INT32_MAX) {
        return 0;
    }
    *output = (int32_t)value;
    return 1;
}

static size_t debug_find_breakpoint(ts_runtime* runtime, const char* module, int32_t line) {
    if (runtime == NULL || module == NULL || line <= 0) {
        return TS_MAX_DEBUG_BREAKPOINTS;
    }

    for (size_t index = 0; index < runtime->debug_breakpoint_count; ++index) {
        if (runtime->debug_breakpoints[index].line == line &&
            runtime->debug_breakpoints[index].module != NULL &&
            strcmp(runtime->debug_breakpoints[index].module, module) == 0) {
            return index;
        }
    }
    return TS_MAX_DEBUG_BREAKPOINTS;
}

static int debug_has_breakpoint(ts_runtime* runtime, const char* module, int32_t line) {
    return debug_find_breakpoint(runtime, module, line) != TS_MAX_DEBUG_BREAKPOINTS;
}

static int debug_parse_location(const char* text, char* module, size_t module_capacity, int32_t* line) {
    if (text == NULL || module == NULL || module_capacity == 0 || line == NULL) {
        return 0;
    }

    while (*text == ' ' || *text == '\t') {
        ++text;
    }

    const char* end = text;
    while (*end != '\0' && *end != '\r' && *end != '\n' && *end != ' ' && *end != '\t') {
        ++end;
    }
    const char* separator = end;
    while (separator > text && *separator != ':') {
        --separator;
    }
    if (separator <= text || separator >= end - 1) {
        return 0;
    }

    size_t module_length = (size_t)(separator - text);
    if (module_length >= module_capacity) {
        return 0;
    }

    char* parse_end = NULL;
    long parsed_line = strtol(separator + 1, &parse_end, 10);
    if (parse_end == separator + 1 || parse_end > end || parsed_line <= 0 || parsed_line > INT32_MAX) {
        return 0;
    }

    memcpy(module, text, module_length);
    module[module_length] = '\0';
    *line = (int32_t)parsed_line;
    return 1;
}

static void debug_send_breakpoints(ts_runtime* runtime, ts_socket client) {
    char response[4096];
    size_t offset = 0;
    offset += (size_t)snprintf(response + offset, sizeof(response) - offset, "{\"breakpoints\":[");
    if (runtime != NULL) {
        for (size_t index = 0; index < runtime->debug_breakpoint_count; ++index) {
            const char* separator = index == 0 ? "" : ",";
            offset += (size_t)snprintf(
                response + offset,
                offset < sizeof(response) ? sizeof(response) - offset : 0,
                "%s{\"module\":\"%s\",\"line\":%d}",
                separator,
                runtime->debug_breakpoints[index].module != NULL ? runtime->debug_breakpoints[index].module : "",
                runtime->debug_breakpoints[index].line
            );
            if (offset >= sizeof(response)) {
                break;
            }
        }
    }
    if (offset < sizeof(response)) {
        snprintf(response + offset, sizeof(response) - offset, "]}\n");
    } else {
        response[sizeof(response) - 2] = '\n';
        response[sizeof(response) - 1] = '\0';
    }
    send(client, response, (int)strlen(response), 0);
}

static void debug_set_breakpoint_response(
    ts_runtime* runtime,
    ts_socket client,
    const char* module,
    int32_t line,
    int json_response
) {
    if (module == NULL || line <= 0) {
        static const char text_error[] = "error invalid breakpoint location\n";
        static const char json_error[] = "{\"ok\":false,\"error\":\"invalid breakpoint location\"}\n";
        send(
            client,
            json_response ? json_error : text_error,
            (int)strlen(json_response ? json_error : text_error),
            0
        );
        return;
    }
    if (debug_has_breakpoint(runtime, module, line)) {
        static const char text_ok[] = "breakpoint already set\n";
        static const char json_ok[] = "{\"ok\":true,\"alreadySet\":true}\n";
        send(client, json_response ? json_ok : text_ok, (int)strlen(json_response ? json_ok : text_ok), 0);
        return;
    }
    if (runtime == NULL || runtime->debug_breakpoint_count >= TS_MAX_DEBUG_BREAKPOINTS) {
        static const char text_error[] = "error breakpoint limit reached\n";
        static const char json_error[] = "{\"ok\":false,\"error\":\"breakpoint limit reached\"}\n";
        send(
            client,
            json_response ? json_error : text_error,
            (int)strlen(json_response ? json_error : text_error),
            0
        );
        return;
    }

    char* module_copy = duplicate_c_string(module);
    if (module_copy == NULL) {
        static const char text_error[] = "error out of memory\n";
        static const char json_error[] = "{\"ok\":false,\"error\":\"out of memory\"}\n";
        send(
            client,
            json_response ? json_error : text_error,
            (int)strlen(json_response ? json_error : text_error),
            0
        );
        return;
    }

    runtime->debug_breakpoints[runtime->debug_breakpoint_count].module = module_copy;
    runtime->debug_breakpoints[runtime->debug_breakpoint_count].line = line;
    ++runtime->debug_breakpoint_count;
    if (json_response) {
        char response[512];
        snprintf(
            response,
            sizeof(response),
            "{\"ok\":true,\"breakpoint\":{\"module\":\"%s\",\"line\":%d}}\n",
            module,
            line
        );
        send(client, response, (int)strlen(response), 0);
    } else {
        static const char ok[] = "breakpoint set\n";
        send(client, ok, (int)(sizeof(ok) - 1), 0);
    }
}

static void debug_set_breakpoint(ts_runtime* runtime, ts_socket client, const char* location) {
    char module[256];
    int32_t line = 0;
    if (!debug_parse_location(location, module, sizeof(module), &line)) {
        static const char error[] = "error invalid breakpoint location\n";
        send(client, error, (int)(sizeof(error) - 1), 0);
        return;
    }
    debug_set_breakpoint_response(runtime, client, module, line, 0);
}

static void debug_clear_breakpoint_response(
    ts_runtime* runtime,
    ts_socket client,
    const char* module,
    int32_t line,
    int json_response
) {
    if (module == NULL || line <= 0) {
        static const char text_error[] = "error invalid breakpoint location\n";
        static const char json_error[] = "{\"ok\":false,\"error\":\"invalid breakpoint location\"}\n";
        send(
            client,
            json_response ? json_error : text_error,
            (int)strlen(json_response ? json_error : text_error),
            0
        );
        return;
    }

    size_t index = debug_find_breakpoint(runtime, module, line);
    if (index == TS_MAX_DEBUG_BREAKPOINTS || runtime == NULL) {
        static const char text_missing[] = "breakpoint not found\n";
        static const char json_missing[] = "{\"ok\":true,\"cleared\":false}\n";
        send(
            client,
            json_response ? json_missing : text_missing,
            (int)strlen(json_response ? json_missing : text_missing),
            0
        );
        return;
    }

    free(runtime->debug_breakpoints[index].module);
    for (size_t cursor = index + 1; cursor < runtime->debug_breakpoint_count; ++cursor) {
        runtime->debug_breakpoints[cursor - 1] = runtime->debug_breakpoints[cursor];
    }
    --runtime->debug_breakpoint_count;
    runtime->debug_breakpoints[runtime->debug_breakpoint_count].module = NULL;
    runtime->debug_breakpoints[runtime->debug_breakpoint_count].line = 0;

    if (json_response) {
        static const char ok[] = "{\"ok\":true,\"cleared\":true}\n";
        send(client, ok, (int)(sizeof(ok) - 1), 0);
    } else {
        static const char ok[] = "breakpoint cleared\n";
        send(client, ok, (int)(sizeof(ok) - 1), 0);
    }
}

static void debug_clear_breakpoint(ts_runtime* runtime, ts_socket client, const char* location) {
    char module[256];
    int32_t line = 0;
    if (!debug_parse_location(location, module, sizeof(module), &line)) {
        static const char error[] = "error invalid breakpoint location\n";
        send(client, error, (int)(sizeof(error) - 1), 0);
        return;
    }
    debug_clear_breakpoint_response(runtime, client, module, line, 0);
}

static void clear_debug_breakpoints(ts_runtime* runtime) {
    if (runtime == NULL) {
        return;
    }

    for (size_t index = 0; index < runtime->debug_breakpoint_count; ++index) {
        free(runtime->debug_breakpoints[index].module);
        runtime->debug_breakpoints[index].module = NULL;
        runtime->debug_breakpoints[index].line = 0;
    }
    runtime->debug_breakpoint_count = 0;
}

static void debug_send_status(ts_runtime* runtime, ts_socket client) {
    char response[1024];
    const char* module_name = runtime != NULL && runtime->debug_paused_module != NULL
        ? runtime->debug_paused_module
        : "";
    const char* function_name = runtime != NULL && runtime->debug_paused_function != NULL
        ? runtime->debug_paused_function
        : "";
    snprintf(
        response,
        sizeof(response),
        "{\"state\":\"%s\",\"pauseId\":%u,\"module\":\"%s\",\"function\":\"%s\",\"line\":%d,\"column\":%d}\n",
        runtime != NULL && runtime->debug_pause_active ? "paused" : "running",
        runtime != NULL && runtime->debug_pause_active ? runtime->debug_paused_id : 0u,
        module_name,
        function_name,
        runtime != NULL ? runtime->debug_paused_line : 0,
        runtime != NULL ? runtime->debug_paused_column : 0
    );
    send(client, response, (int)strlen(response), 0);
}

static void debug_send_variables(ts_runtime* runtime, ts_socket client) {
    const char* variables = runtime != NULL && runtime->debug_paused_variables_json != NULL
        ? runtime->debug_paused_variables_json
        : "{}";
    char prefix[] = "{\"variables\":";
    char suffix[] = "}\n";
    send(client, prefix, (int)(sizeof(prefix) - 1), 0);
    send(client, variables, (int)strlen(variables), 0);
    send(client, suffix, (int)(sizeof(suffix) - 1), 0);
}

static char* debug_json_escape_string(const char* text) {
    if (text == NULL) {
        return duplicate_c_string("");
    }

    size_t length = strlen(text);
    char* escaped = malloc(length * 6 + 1);
    if (escaped == NULL) {
        return duplicate_c_string("");
    }

    char* cursor = escaped;
    for (size_t index = 0; index < length; ++index) {
        unsigned char character = (unsigned char)text[index];
        switch (character) {
            case '\\':
                *cursor++ = '\\';
                *cursor++ = '\\';
                break;
            case '"':
                *cursor++ = '\\';
                *cursor++ = '"';
                break;
            case '\n':
                *cursor++ = '\\';
                *cursor++ = 'n';
                break;
            case '\r':
                *cursor++ = '\\';
                *cursor++ = 'r';
                break;
            case '\t':
                *cursor++ = '\\';
                *cursor++ = 't';
                break;
            default:
                if (character < 0x20) {
                    snprintf(cursor, 7, "\\u%04x", character);
                    cursor += 6;
                } else {
                    *cursor++ = (char)character;
                }
                break;
        }
    }
    *cursor = '\0';
    return escaped;
}

static void debug_send_stack(ts_runtime* runtime, ts_socket client) {
    const char* stack = runtime != NULL && runtime->debug_paused_stack != NULL
        ? runtime->debug_paused_stack
        : "";
    char* escaped = debug_json_escape_string(stack);
    if (escaped == NULL) {
        static const char fallback[] = "{\"stack\":\"\"}\n";
        send(client, fallback, (int)(sizeof(fallback) - 1), 0);
        return;
    }

    static const char prefix[] = "{\"stack\":\"";
    static const char suffix[] = "\"}\n";
    send(client, prefix, (int)(sizeof(prefix) - 1), 0);
    send(client, escaped, (int)strlen(escaped), 0);
    send(client, suffix, (int)(sizeof(suffix) - 1), 0);
    free(escaped);
}

static void debug_continue_runtime(ts_runtime* runtime) {
    if (runtime == NULL) {
        return;
    }
    runtime->debug_step_requested = 0;
    runtime->debug_step_mode = TS_DEBUG_STEP_NONE;
    runtime->debug_step_target_stack_depth = 0;
    ++runtime->debug_continue_counter;
}

static void debug_step_runtime(ts_runtime* runtime, ts_debug_step_mode mode) {
    if (runtime == NULL) {
        return;
    }

    runtime->debug_step_requested = 1;
    runtime->debug_step_mode = mode;
    runtime->debug_step_target_stack_depth = runtime->debug_paused_stack_depth;
    ++runtime->debug_continue_counter;
}

static int debug_receive_command(ts_socket client, char* buffer, size_t buffer_size) {
    if (buffer == NULL || buffer_size == 0) {
        return 0;
    }

    fd_set read_set;
    FD_ZERO(&read_set);
    FD_SET(client, &read_set);

    struct timeval timeout;
    timeout.tv_sec = 0;
    timeout.tv_usec = 250000;

#if defined(_WIN32)
    int ready = select(0, &read_set, NULL, NULL, &timeout);
#else
    int ready = select(client + 1, &read_set, NULL, NULL, &timeout);
#endif
    if (ready <= 0) {
        return 0;
    }

    int read_count = recv(client, buffer, (int)(buffer_size - 1), 0);
    if (read_count <= 0) {
        return 0;
    }
    buffer[read_count] = '\0';
    return read_count;
}

static void debug_handle_json_command(ts_runtime* runtime, ts_socket client, const char* command) {
    char command_name[64];
    if (!debug_json_string_value(command, "command", command_name, sizeof(command_name))) {
        static const char error[] = "{\"ok\":false,\"error\":\"missing command\"}\n";
        send(client, error, (int)(sizeof(error) - 1), 0);
        return;
    }

    if (strcmp(command_name, "status") == 0) {
        debug_send_status(runtime, client);
    } else if (strcmp(command_name, "continue") == 0) {
        debug_continue_runtime(runtime);
        debug_log(runtime, "AriadneTS debug continue requested");
        static const char response[] = "{\"ok\":true,\"continued\":true}\n";
        send(client, response, (int)(sizeof(response) - 1), 0);
    } else if (strcmp(command_name, "step") == 0 || strcmp(command_name, "stepIn") == 0) {
        debug_step_runtime(runtime, TS_DEBUG_STEP_IN);
        debug_log(runtime, "AriadneTS debug step in requested");
        static const char response[] = "{\"ok\":true,\"continued\":true,\"step\":true}\n";
        send(client, response, (int)(sizeof(response) - 1), 0);
    } else if (strcmp(command_name, "next") == 0) {
        debug_step_runtime(runtime, TS_DEBUG_STEP_OVER);
        debug_log(runtime, "AriadneTS debug next requested");
        static const char response[] = "{\"ok\":true,\"continued\":true,\"step\":true}\n";
        send(client, response, (int)(sizeof(response) - 1), 0);
    } else if (strcmp(command_name, "stepOut") == 0) {
        debug_step_runtime(runtime, TS_DEBUG_STEP_OUT);
        debug_log(runtime, "AriadneTS debug step out requested");
        static const char response[] = "{\"ok\":true,\"continued\":true,\"step\":true}\n";
        send(client, response, (int)(sizeof(response) - 1), 0);
    } else if (strcmp(command_name, "listBreakpoints") == 0) {
        debug_send_breakpoints(runtime, client);
    } else if (strcmp(command_name, "variables") == 0) {
        debug_send_variables(runtime, client);
    } else if (strcmp(command_name, "stack") == 0) {
        debug_send_stack(runtime, client);
    } else if (strcmp(command_name, "setBreakpoint") == 0) {
        char module[256];
        int32_t line = 0;
        int has_module = debug_json_string_value(command, "module", module, sizeof(module));
        int has_line = debug_json_int_value(command, "line", &line);
        debug_set_breakpoint_response(
            runtime,
            client,
            has_module ? module : NULL,
            has_line ? line : 0,
            1
        );
    } else if (strcmp(command_name, "clearBreakpoint") == 0) {
        char module[256];
        int32_t line = 0;
        int has_module = debug_json_string_value(command, "module", module, sizeof(module));
        int has_line = debug_json_int_value(command, "line", &line);
        debug_clear_breakpoint_response(
            runtime,
            client,
            has_module ? module : NULL,
            has_line ? line : 0,
            1
        );
    } else {
        static const char error[] = "{\"ok\":false,\"error\":\"unknown command\"}\n";
        send(client, error, (int)(sizeof(error) - 1), 0);
    }
}

static int debug_bind_socket(ts_runtime* runtime) {
    runtime->debug_listen_socket = TS_INVALID_SOCKET;

#if defined(_WIN32)
    WSADATA wsa_data;
    if (WSAStartup(MAKEWORD(2, 2), &wsa_data) != 0) {
        debug_log(runtime, "AriadneTS debug endpoint failed: WSAStartup failed");
        return 0;
    }
#endif

    ts_socket server = socket(AF_INET, SOCK_STREAM, 0);
    if (server == TS_INVALID_SOCKET) {
        debug_log(runtime, "AriadneTS debug endpoint failed: socket creation failed");
#if defined(_WIN32)
        WSACleanup();
#endif
        return 0;
    }

    int reuse = 1;
    setsockopt(server, SOL_SOCKET, SO_REUSEADDR, (const char*)&reuse, sizeof(reuse));

    struct sockaddr_in address;
    memset(&address, 0, sizeof(address));
    address.sin_family = AF_INET;
    address.sin_port = htons(runtime->debug_port);
    if (inet_pton(AF_INET, runtime->debug_host, &address.sin_addr) != 1) {
        (void)inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
    }

    if (bind(server, (struct sockaddr*)&address, sizeof(address)) != 0) {
        ts_close_socket(server);
        debug_log(runtime, "AriadneTS debug endpoint failed: bind failed");
#if defined(_WIN32)
        WSACleanup();
#endif
        return 0;
    }

    if (listen(server, 1) != 0) {
        ts_close_socket(server);
        debug_log(runtime, "AriadneTS debug endpoint failed: listen failed");
#if defined(_WIN32)
        WSACleanup();
#endif
        return 0;
    }

    runtime->debug_listen_socket = server;
    debug_log(runtime, "AriadneTS debug endpoint is listening");
    return 1;
}

static void debug_accept_loop(ts_runtime* runtime) {
    static const char greeting[] =
        "AriadneTS debug endpoint connected.\n"
        "Commands: status, continue, step, next, stepIn, stepOut, variables, stack, break <file>:<line>, clear <file>:<line>, breakpoints\n";

    while (runtime != NULL && !runtime->debug_stop_requested) {
        ts_socket client = accept(runtime->debug_listen_socket, NULL, NULL);
        if (client == TS_INVALID_SOCKET) {
            if (runtime->debug_stop_requested) {
                break;
            }
            continue;
        }

        runtime->debug_client_attached = 1;
        send(client, greeting, (int)(sizeof(greeting) - 1), 0);

        char command[128];
        if (debug_receive_command(client, command, sizeof(command))) {
            if (command[0] == '{') {
                debug_handle_json_command(runtime, client, command);
            } else if (debug_command_contains(command, "status")) {
                debug_send_status(runtime, client);
            } else if (debug_command_contains(command, "breakpoints")) {
                debug_send_breakpoints(runtime, client);
            } else if (debug_command_contains(command, "variables")) {
                debug_send_variables(runtime, client);
            } else if (debug_command_contains(command, "stack")) {
                debug_send_stack(runtime, client);
            } else if (strncmp(command, "break ", 6) == 0) {
                debug_set_breakpoint(runtime, client, command + 6);
            } else if (strncmp(command, "clear ", 6) == 0) {
                debug_clear_breakpoint(runtime, client, command + 6);
            } else if (debug_command_contains(command, "continue")) {
                debug_continue_runtime(runtime);
                debug_log(runtime, "AriadneTS debug continue requested");
                static const char continued[] = "continued\n";
                send(client, continued, (int)(sizeof(continued) - 1), 0);
            } else if (debug_command_contains(command, "next")) {
                debug_step_runtime(runtime, TS_DEBUG_STEP_OVER);
                debug_log(runtime, "AriadneTS debug next requested");
                static const char stepped[] = "stepped\n";
                send(client, stepped, (int)(sizeof(stepped) - 1), 0);
            } else if (debug_command_contains(command, "stepOut")) {
                debug_step_runtime(runtime, TS_DEBUG_STEP_OUT);
                debug_log(runtime, "AriadneTS debug step out requested");
                static const char stepped[] = "stepped\n";
                send(client, stepped, (int)(sizeof(stepped) - 1), 0);
            } else if (debug_command_contains(command, "step")) {
                debug_step_runtime(runtime, TS_DEBUG_STEP_IN);
                debug_log(runtime, "AriadneTS debug step requested");
                static const char stepped[] = "stepped\n";
                send(client, stepped, (int)(sizeof(stepped) - 1), 0);
            } else {
                static const char unknown[] = "unknown command\n";
                send(client, unknown, (int)(sizeof(unknown) - 1), 0);
            }
        }
        ts_close_socket(client);
    }
}

#if defined(_WIN32)
static DWORD WINAPI debug_thread_main(LPVOID parameter) {
    debug_accept_loop((ts_runtime*)parameter);
    return 0;
}
#else
static void* debug_thread_main(void* parameter) {
    debug_accept_loop((ts_runtime*)parameter);
    return NULL;
}
#endif

static void start_debug_endpoint(ts_runtime* runtime) {
    if (runtime == NULL || !runtime->debug_enabled || runtime->debug_port == 0) {
        return;
    }
    if (!debug_bind_socket(runtime)) {
        return;
    }

#if defined(_WIN32)
    runtime->debug_thread = CreateThread(NULL, 0, debug_thread_main, runtime, 0, NULL);
    if (runtime->debug_thread == NULL) {
        ts_close_socket(runtime->debug_listen_socket);
        runtime->debug_listen_socket = TS_INVALID_SOCKET;
#if defined(_WIN32)
        WSACleanup();
#endif
        debug_log(runtime, "AriadneTS debug endpoint failed: thread creation failed");
        return;
    }
#else
    if (pthread_create(&runtime->debug_thread, NULL, debug_thread_main, runtime) != 0) {
        ts_close_socket(runtime->debug_listen_socket);
        runtime->debug_listen_socket = TS_INVALID_SOCKET;
        debug_log(runtime, "AriadneTS debug endpoint failed: thread creation failed");
        return;
    }
    runtime->debug_thread_started = 1;
#endif

    if (runtime->debug_wait_for_attach) {
        debug_log(runtime, "AriadneTS waiting for a debug client to attach");
        while (!runtime->debug_stop_requested && !runtime->debug_client_attached) {
            sleep_milliseconds(50);
        }
    }
}

static void wake_debug_accept(ts_runtime* runtime) {
    if (runtime == NULL || runtime->debug_host == NULL || runtime->debug_port == 0) {
        return;
    }

    ts_socket client = socket(AF_INET, SOCK_STREAM, 0);
    if (client == TS_INVALID_SOCKET) {
        return;
    }

    struct sockaddr_in address;
    memset(&address, 0, sizeof(address));
    address.sin_family = AF_INET;
    address.sin_port = htons(runtime->debug_port);
    if (inet_pton(AF_INET, runtime->debug_host, &address.sin_addr) != 1) {
        (void)inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
    }

    (void)connect(client, (struct sockaddr*)&address, sizeof(address));
    ts_close_socket(client);
}

static void stop_debug_endpoint(ts_runtime* runtime) {
    if (runtime == NULL || !runtime->debug_enabled) {
        return;
    }

    runtime->debug_stop_requested = 1;
    wake_debug_accept(runtime);
    if (runtime->debug_listen_socket != TS_INVALID_SOCKET) {
        ts_close_socket(runtime->debug_listen_socket);
        runtime->debug_listen_socket = TS_INVALID_SOCKET;
    }

#if defined(_WIN32)
    if (runtime->debug_thread != NULL) {
        WaitForSingleObject(runtime->debug_thread, 1000);
        CloseHandle(runtime->debug_thread);
        runtime->debug_thread = NULL;
    }
    WSACleanup();
#else
    if (runtime->debug_thread_started) {
        pthread_join(runtime->debug_thread, NULL);
        runtime->debug_thread_started = 0;
    }
#endif
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

static JSValue host_invoke(
    JSContext* context,
    JSValueConst this_value,
    int argument_count,
    JSValueConst* arguments
) {
    (void)this_value;

    ts_runtime* runtime = JS_GetContextOpaque(context);
    if (runtime == NULL || runtime->host_invoke_callback == NULL) {
        return JS_ThrowInternalError(context, "no host invoke callback is configured");
    }
    if (argument_count < 1) {
        return JS_ThrowTypeError(context, "host.invoke requires a method name");
    }

    size_t method_length = 0;
    const char* method = JS_ToCStringLen(context, &method_length, arguments[0]);
    if (method == NULL) {
        return JS_EXCEPTION;
    }

    JSValue payload = argument_count > 1 ? JS_DupValue(context, arguments[1]) : JS_NULL;
    JSValue payload_json_value = JS_JSONStringify(context, payload, JS_UNDEFINED, JS_UNDEFINED);
    JS_FreeValue(context, payload);
    if (JS_IsException(payload_json_value)) {
        JS_FreeCString(context, method);
        return JS_EXCEPTION;
    }
    if (JS_IsUndefined(payload_json_value)) {
        JS_FreeValue(context, payload_json_value);
        payload_json_value = JS_NewString(context, "null");
    }

    size_t payload_json_length = 0;
    const char* payload_json = JS_ToCStringLen(
        context,
        &payload_json_length,
        payload_json_value
    );
    if (payload_json == NULL) {
        JS_FreeValue(context, payload_json_value);
        JS_FreeCString(context, method);
        return JS_EXCEPTION;
    }

    size_t result_length = 0;
    ts_status status = runtime->host_invoke_callback(
        runtime->host_invoke_user_data,
        method,
        method_length,
        payload_json,
        payload_json_length,
        NULL,
        0,
        &result_length
    );
    char* result_json = NULL;
    if (status == TS_STATUS_OK) {
        result_json = malloc(result_length + 1);
        if (result_json == NULL) {
            status = TS_STATUS_OUT_OF_MEMORY;
        }
    }
    if (status == TS_STATUS_OK) {
        size_t written_length = result_length;
        status = runtime->host_invoke_callback(
            runtime->host_invoke_user_data,
            method,
            method_length,
            payload_json,
            payload_json_length,
            result_json,
            result_length,
            &written_length
        );
        if (status == TS_STATUS_OK && written_length <= result_length) {
            result_length = written_length;
            result_json[result_length] = '\0';
        } else if (status == TS_STATUS_OK) {
            status = TS_STATUS_BUFFER_TOO_SMALL;
        }
    }

    JS_FreeCString(context, payload_json);
    JS_FreeValue(context, payload_json_value);
    JS_FreeCString(context, method);
    if (status != TS_STATUS_OK) {
        free(result_json);
        return JS_ThrowInternalError(context, "host invoke failed with status %d", status);
    }

    JSValue result = JS_ParseJSON(context, result_json, result_length, "<host-result>");
    free(result_json);
    return result;
}

static char* debug_stringify_value(JSContext* context, JSValueConst value) {
    JSValue json = JS_JSONStringify(context, value, JS_UNDEFINED, JS_UNDEFINED);
    if (JS_IsException(json) || JS_IsUndefined(json)) {
        JS_FreeValue(context, json);
        return duplicate_c_string("{}");
    }

    const char* text = JS_ToCString(context, json);
    char* copy = duplicate_c_string(text != NULL ? text : "{}");
    if (text != NULL) {
        JS_FreeCString(context, text);
    }
    JS_FreeValue(context, json);
    return copy;
}

static char* debug_variables_json(JSContext* context, JSValueConst value) {
    if (JS_IsUndefined(value) || JS_IsNull(value)) {
        return duplicate_c_string("{}");
    }

    if (!JS_IsFunction(context, value)) {
        return debug_stringify_value(context, value);
    }

    JSValue snapshot = JS_Call(context, value, JS_UNDEFINED, 0, NULL);
    if (JS_IsException(snapshot)) {
        JSValue exception = JS_GetException(context);
        JS_FreeValue(context, exception);
        return duplicate_c_string("{\"<error>\":\"<unavailable>\"}");
    }

    char* json = debug_stringify_value(context, snapshot);
    JS_FreeValue(context, snapshot);
    return json;
}

static void debug_pause_at(
    ts_runtime* runtime,
    const char* module_name,
    int32_t line,
    int32_t column,
    const char* function_name,
    const char* variables_json,
    const char* stack
) {
    if (runtime == NULL || !runtime->debug_enabled) {
        return;
    }

    clear_debug_pause_location(runtime);
    runtime->debug_paused_module = duplicate_c_string(module_name != NULL ? module_name : "<unknown>");
    runtime->debug_paused_function = duplicate_c_string(function_name != NULL ? function_name : "AriadneTS");
    runtime->debug_paused_variables_json = duplicate_c_string(variables_json != NULL ? variables_json : "{}");
    runtime->debug_paused_stack = duplicate_c_string(stack != NULL ? stack : "");
    runtime->debug_paused_stack_depth = debug_stack_depth(stack);
    runtime->debug_paused_id = ++runtime->debug_pause_sequence;
    runtime->debug_paused_line = line;
    runtime->debug_paused_column = column;
    runtime->debug_pause_active = 1;
    runtime->debug_step_requested = 0;
    runtime->debug_step_mode = TS_DEBUG_STEP_NONE;
    runtime->debug_step_target_stack_depth = 0;

    char message[512];
    snprintf(
        message,
        sizeof(message),
        "AriadneTS paused at %s:%d:%d. Send 'continue' or 'step' to the debug port to resume.",
        module_name != NULL ? module_name : "<unknown>",
        line,
        column
    );
    debug_log(runtime, message);

    uint64_t paused_deadline = runtime->deadline_nanoseconds;
    uint64_t paused_remaining_nanoseconds = 0;
    if (paused_deadline != 0) {
        uint64_t now = monotonic_nanoseconds();
        paused_remaining_nanoseconds = paused_deadline > now
            ? paused_deadline - now
            : 1;
        runtime->deadline_nanoseconds = 0;
    }

    uint32_t observed_continue_counter = runtime->debug_continue_counter;
    while (!runtime->debug_stop_requested &&
        runtime->debug_continue_counter == observed_continue_counter) {
        sleep_milliseconds(50);
    }

    if (paused_deadline != 0) {
        runtime->deadline_nanoseconds = runtime->debug_stop_requested
            ? paused_deadline
            : monotonic_nanoseconds() + paused_remaining_nanoseconds;
    }

    clear_debug_pause_location(runtime);
}

static int debug_should_pause_for_step(ts_runtime* runtime, int32_t stack_depth) {
    if (runtime == NULL || !runtime->debug_step_requested) {
        return 0;
    }

    switch ((ts_debug_step_mode)runtime->debug_step_mode) {
        case TS_DEBUG_STEP_IN:
            return 1;
        case TS_DEBUG_STEP_OVER:
            return runtime->debug_step_target_stack_depth <= 0 ||
                stack_depth == 0 ||
                stack_depth <= runtime->debug_step_target_stack_depth;
        case TS_DEBUG_STEP_OUT:
            return runtime->debug_step_target_stack_depth <= 1 ||
                stack_depth == 0 ||
                stack_depth < runtime->debug_step_target_stack_depth;
        case TS_DEBUG_STEP_NONE:
        default:
            return runtime->debug_step_requested;
    }
}

static JSValue debug_checkpoint(
    JSContext* context,
    JSValueConst this_value,
    int argument_count,
    JSValueConst* arguments
) {
    (void)this_value;

    ts_runtime* runtime = JS_GetContextOpaque(context);
    if (runtime == NULL || !runtime->debug_enabled) {
        return JS_UNDEFINED;
    }

    const char* module_name = argument_count > 0
        ? JS_ToCString(context, arguments[0])
        : NULL;
    int32_t line = 0;
    int32_t column = 0;
    if (argument_count > 1) {
        JS_ToInt32(context, &line, arguments[1]);
    }
    if (argument_count > 2) {
        JS_ToInt32(context, &column, arguments[2]);
    }

    const char* function_name = argument_count > 3
        ? JS_ToCString(context, arguments[3])
        : NULL;
    char* variables_json = argument_count > 4
        ? debug_variables_json(context, arguments[4])
        : duplicate_c_string("{}");
    const char* stack = argument_count > 5
        ? JS_ToCString(context, arguments[5])
        : NULL;

    debug_pause_at(
        runtime,
        module_name,
        line,
        column,
        function_name != NULL ? function_name : "debugger",
        variables_json,
        stack);
    free(variables_json);
    if (stack != NULL) {
        JS_FreeCString(context, stack);
    }
    if (function_name != NULL) {
        JS_FreeCString(context, function_name);
    }
    if (module_name != NULL) {
        JS_FreeCString(context, module_name);
    }
    return JS_UNDEFINED;
}

static JSValue debug_line_probe(
    JSContext* context,
    JSValueConst this_value,
    int argument_count,
    JSValueConst* arguments
) {
    (void)this_value;

    ts_runtime* runtime = JS_GetContextOpaque(context);
    if (runtime == NULL || !runtime->debug_enabled) {
        return JS_UNDEFINED;
    }

    const char* module_name = argument_count > 0
        ? JS_ToCString(context, arguments[0])
        : NULL;
    int32_t line = 0;
    int32_t column = 0;
    if (argument_count > 1) {
        JS_ToInt32(context, &line, arguments[1]);
    }
    if (argument_count > 2) {
        JS_ToInt32(context, &column, arguments[2]);
    }

    const char* function_name = argument_count > 3
        ? JS_ToCString(context, arguments[3])
        : NULL;
    const char* stack = argument_count > 5
        ? JS_ToCString(context, arguments[5])
        : NULL;

    int32_t stack_depth = debug_stack_depth(stack);
    if (debug_has_breakpoint(runtime, module_name, line) ||
        debug_should_pause_for_step(runtime, stack_depth)) {
        char* variables_json = argument_count > 4
            ? debug_variables_json(context, arguments[4])
            : duplicate_c_string("{}");
        debug_pause_at(runtime, module_name, line, column, function_name, variables_json, stack);
        free(variables_json);
    }

    if (stack != NULL) {
        JS_FreeCString(context, stack);
    }
    if (function_name != NULL) {
        JS_FreeCString(context, function_name);
    }
    if (module_name != NULL) {
        JS_FreeCString(context, module_name);
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
    JSValue invoke = JS_NewCFunction(context, host_invoke, "invoke", 2);
    JSValue checkpoint = JS_NewCFunction(
        context,
        debug_checkpoint,
        "__ariadnets_debug_checkpoint",
        6
    );
    JSValue line_probe = JS_NewCFunction(
        context,
        debug_line_probe,
        "__ariadnets_debug_line",
        6
    );

    if (JS_IsException(global) || JS_IsException(host) ||
        JS_IsException(log) || JS_IsException(invoke) ||
        JS_IsException(checkpoint) || JS_IsException(line_probe)) {
        JS_FreeValue(context, line_probe);
        JS_FreeValue(context, checkpoint);
        JS_FreeValue(context, invoke);
        JS_FreeValue(context, log);
        JS_FreeValue(context, host);
        JS_FreeValue(context, global);
        return 0;
    }

    JS_SetPropertyStr(context, host, "log", log);
    JS_SetPropertyStr(context, host, "invoke", invoke);
    JS_SetPropertyStr(context, global, "host", host);
    JS_SetPropertyStr(context, global, "__ariadnets_debug_checkpoint", checkpoint);
    JS_SetPropertyStr(context, global, "__ariadnets_debug_line", line_probe);
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
    runtime->host_invoke_callback = config->host_invoke_callback;
    runtime->host_invoke_user_data = config->host_invoke_user_data;
    runtime->execution_timeout_nanoseconds =
        (uint64_t)config->execution_timeout_milliseconds * 1000000ull;
    runtime->debug_listen_socket = TS_INVALID_SOCKET;
    if (config_has_field(config, offsetof(ts_runtime_config, debug_wait_for_attach) + sizeof(config->debug_wait_for_attach))) {
        runtime->debug_enabled = config->debug_enabled;
        runtime->debug_protocol = config->debug_protocol;
        runtime->debug_host = duplicate_c_string(
            config->debug_host != NULL && config->debug_host[0] != '\0'
                ? config->debug_host
                : "127.0.0.1");
        runtime->debug_port = config->debug_port;
        runtime->debug_wait_for_attach = config->debug_wait_for_attach;
        if (runtime->debug_enabled && runtime->debug_host == NULL) {
            ts_runtime_destroy(runtime);
            return NULL;
        }
    }
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

    start_debug_endpoint(runtime);
    return runtime;
}

void ts_runtime_destroy(ts_runtime* runtime) {
    if (runtime == NULL) {
        return;
    }

    stop_debug_endpoint(runtime);
    clear_last_error(runtime);
    clear_last_result(runtime);
    clear_unhandled_rejection(runtime);
    clear_debug_pause_location(runtime);
    clear_debug_breakpoints(runtime);
    free(runtime->debug_host);
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
