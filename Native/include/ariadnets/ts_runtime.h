#ifndef TSRUNTIME_TS_RUNTIME_H
#define TSRUNTIME_TS_RUNTIME_H

#include <stddef.h>
#include <stdint.h>

#include "ariadnets/ts_export.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct ts_runtime ts_runtime;

typedef enum ts_status {
    TS_STATUS_OK = 0,
    TS_STATUS_INVALID_ARGUMENT = 1,
    TS_STATUS_OUT_OF_MEMORY = 2,
    TS_STATUS_SCRIPT_ERROR = 3,
    TS_STATUS_INTERNAL_ERROR = 4,
    TS_STATUS_MODULE_NOT_FOUND = 5,
    TS_STATUS_BUFFER_TOO_SMALL = 6,
    TS_STATUS_HOST_ERROR = 7
} ts_status;

typedef void (*ts_log_callback)(
    void* user_data,
    const char* message,
    size_t message_length
);

typedef ts_status (*ts_module_load_callback)(
    void* user_data,
    const char* module_name,
    size_t module_name_length,
    char* source_buffer,
    size_t source_capacity,
    size_t* source_length
);

typedef ts_status (*ts_host_invoke_callback)(
    void* user_data,
    const char* method,
    size_t method_length,
    const char* payload_json,
    size_t payload_json_length,
    char* result_buffer,
    size_t result_capacity,
    size_t* result_length
);

typedef struct ts_runtime_config {
    uint32_t struct_size;
    uint64_t memory_limit_bytes;
    ts_log_callback log_callback;
    void* log_user_data;
    ts_module_load_callback module_load_callback;
    void* module_load_user_data;
    uint64_t max_stack_size_bytes;
    uint32_t execution_timeout_milliseconds;
    ts_host_invoke_callback host_invoke_callback;
    void* host_invoke_user_data;
} ts_runtime_config;

TSRUNTIME_API uint32_t ts_runtime_abi_version(void);

TSRUNTIME_API ts_runtime* ts_runtime_create(const ts_runtime_config* config);

TSRUNTIME_API void ts_runtime_destroy(ts_runtime* runtime);

TSRUNTIME_API ts_status ts_runtime_eval(
    ts_runtime* runtime,
    const char* source,
    size_t source_length,
    const char* filename
);

TSRUNTIME_API ts_status ts_runtime_eval_module(
    ts_runtime* runtime,
    const char* source,
    size_t source_length,
    const char* module_name
);

TSRUNTIME_API ts_status ts_runtime_execute_pending_jobs(
    ts_runtime* runtime,
    uint32_t max_jobs,
    uint32_t* executed_jobs
);

TSRUNTIME_API ts_status ts_runtime_invoke(
    ts_runtime* runtime,
    const char* method,
    size_t method_length,
    const char* payload_json,
    size_t payload_json_length
);

TSRUNTIME_API const char* ts_runtime_last_result(
    const ts_runtime* runtime,
    size_t* result_length
);

TSRUNTIME_API const char* ts_runtime_last_error(
    const ts_runtime* runtime,
    size_t* error_length
);

#ifdef __cplusplus
}
#endif

#endif
