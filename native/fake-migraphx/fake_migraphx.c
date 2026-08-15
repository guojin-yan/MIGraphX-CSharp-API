#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#if defined(_WIN32)
#include <windows.h>
#define EXPORT __declspec(dllexport)
#define ATOMIC_INCREMENT(value) InterlockedIncrement((volatile LONG*)&(value))
#define ATOMIC_DECREMENT(value) InterlockedDecrement((volatile LONG*)&(value))
#define ATOMIC_EXCHANGE(value, replacement) InterlockedExchange((volatile LONG*)&(value), (replacement))
#else
#define EXPORT __attribute__((visibility("default")))
#define ATOMIC_INCREMENT(value) __atomic_add_fetch(&(value), 1, __ATOMIC_SEQ_CST)
#define ATOMIC_DECREMENT(value) __atomic_sub_fetch(&(value), 1, __ATOMIC_SEQ_CST)
#define ATOMIC_EXCHANGE(value, replacement) __atomic_exchange_n(&(value), (replacement), __ATOMIC_SEQ_CST)
#endif

typedef enum
{
    migraphx_status_success        = 0,
    migraphx_status_bad_param      = 1,
    migraphx_status_unknown_target = 3,
    migraphx_status_unknown_error  = 4
} migraphx_status;

typedef struct fake_target { int value; } *migraphx_target_t;
typedef struct fake_dynamic_dimension
{
    size_t minimum;
    size_t maximum;
    size_t optimal_count;
    size_t optimals[8];
} fake_dynamic_dimension;
typedef fake_dynamic_dimension* migraphx_dynamic_dimension_t;
typedef const fake_dynamic_dimension* const_migraphx_dynamic_dimension_t;
typedef struct fake_dynamic_dimensions
{
    size_t count;
    size_t size_calls;
    fake_dynamic_dimension* values[8];
} *migraphx_dynamic_dimensions_t;
typedef struct fake_optimals { size_t count; size_t values[8]; } *migraphx_optimals_t;
typedef struct fake_file_options { char format[32]; } *migraphx_file_options_t;
typedef struct fake_options
{
    int value;
    int dynamic;
    size_t dynamic_count;
    fake_dynamic_dimension dynamic_values[8];
    int static_override;
    size_t static_count;
    size_t static_values[8];
    int64_t default_loop_iterations;
    int64_t limit_loop_iterations;
    char external_data_path[512];
} *migraphx_onnx_options_t;
typedef struct fake_program { int value; int compiled; int offload_copy; int dynamic; size_t dynamic_count; fake_dynamic_dimension dynamic_values[8]; size_t static_count; size_t static_values[8]; } *migraphx_program_t;
typedef struct fake_compile_options { uint8_t offload_copy; uint8_t fast_math; uint8_t exhaustive_tune; } *migraphx_compile_options_t;
typedef struct fake_shape
{
    int type;
    size_t rank;
    size_t lengths[8];
    size_t strides[8];
    size_t elements;
    size_t bytes;
    uint8_t standard;
    uint8_t dynamic;
    size_t dynamic_count;
    fake_dynamic_dimension dynamic_values[8];
} fake_shape;
typedef fake_shape* migraphx_shape_t;
typedef struct fake_parameter_shapes { fake_shape shape; } *migraphx_program_parameter_shapes_t;
typedef struct fake_shapes { fake_shape shape; } *migraphx_shapes_t;
typedef struct fake_argument { fake_shape shape; char* buffer; int owns_buffer; } *migraphx_argument_t;
typedef struct fake_program_parameters
{
    struct fake_argument arguments[4];
    char names[4][64];
    size_t count;
} *migraphx_program_parameters_t;
typedef struct fake_arguments { struct fake_argument argument; void* stream; const char* queued_source; int pending; } *migraphx_arguments_t;
typedef migraphx_status (*fake_m3_callback)(
    void* state,
    const char* text,
    size_t text_size,
    uint8_t flag,
    const void* borrowed,
    void** out_handle);

static volatile int32_t next_status;
static volatile int32_t create_null;
static volatile int32_t target_destroy_count;
static volatile int32_t program_destroy_count;
static volatile int32_t target_assign_count;
static volatile int32_t program_assign_count;
static volatile int32_t target_live_count;
static volatile int32_t program_live_count;
static volatile int32_t target_assign_copied;
static volatile int32_t program_assign_copied;
static volatile int32_t next_value = 100;
static volatile int32_t target_name_lock;
static char last_target_name[512];
static char last_model_path[512];
static volatile int32_t last_model_size;
static volatile int32_t parse_file_count;
static volatile int32_t parse_buffer_count;
static volatile int32_t compile_count;
static volatile int32_t run_count;
static volatile int32_t m2_destroy_count;
static volatile int32_t m2_live_count;
static volatile int32_t m5_live_count;
static volatile int32_t m5_destroy_count;
static volatile int32_t shape_mode;
static volatile int32_t shape_type_override = -1;
static volatile int32_t parameter_size_reads;
static volatile int32_t argument_size_reads;
static volatile int32_t last_parameter_count;
static volatile int64_t last_default_loop_iterations;
static volatile int64_t last_limit_loop_iterations;
static volatile int32_t last_fast_math;
static volatile int32_t last_exhaustive_tune;
static char last_external_data_path[512];
static char failure_entry[128];
static volatile int32_t failure_status;
static char null_entry[128];
static fake_m3_callback m3_callback;
static void* m3_callback_state;
static migraphx_arguments_t async_runs[64];
static volatile int32_t async_run_count;
static volatile int32_t async_complete_count;
static void* last_async_stream;
static void* last_async_input;
static char last_async_name[64];

static int take_status(void)
{
    return ATOMIC_EXCHANGE(next_status, 0);
}

static int take_status_for(const char* entry)
{
    if(failure_entry[0] != '\0' && strcmp(failure_entry, entry) == 0)
    {
        failure_entry[0] = '\0';
        return ATOMIC_EXCHANGE(failure_status, 0);
    }
    return take_status();
}

static int take_null_for(const char* entry)
{
    if(null_entry[0] != '\0' && strcmp(null_entry, entry) == 0)
    {
        null_entry[0] = '\0';
        return 1;
    }
    return ATOMIC_EXCHANGE(create_null, 0);
}

static void copy_string(char* destination, size_t capacity, const char* source)
{
    size_t length = strlen(source);
    if(length >= capacity)
        length = capacity - 1;
    memcpy(destination, source, length);
    destination[length] = '\0';
}

static void lock_target_name(void)
{
    while(ATOMIC_EXCHANGE(target_name_lock, 1) != 0)
    {
    }
}

static void unlock_target_name(void)
{
    ATOMIC_EXCHANGE(target_name_lock, 0);
}

EXPORT void fake_reset(void)
{
    next_status = 0;
    create_null = 0;
    target_destroy_count = 0;
    program_destroy_count = 0;
    target_assign_count = 0;
    program_assign_count = 0;
    target_live_count = 0;
    program_live_count = 0;
    target_assign_copied = 0;
    program_assign_copied = 0;
    next_value = 100;
    target_name_lock = 0;
    last_target_name[0] = '\0';
    last_model_path[0] = '\0';
    last_model_size = 0;
    parse_file_count = 0;
    parse_buffer_count = 0;
    compile_count = 0;
    run_count = 0;
    m2_destroy_count = 0;
    m2_live_count = 0;
    m5_live_count = 0;
    m5_destroy_count = 0;
    shape_mode = 0;
    shape_type_override = -1;
    parameter_size_reads = 0;
    argument_size_reads = 0;
    last_parameter_count = 0;
    last_default_loop_iterations = 0;
    last_limit_loop_iterations = 0;
    last_fast_math = 0;
    last_exhaustive_tune = 0;
    last_external_data_path[0] = '\0';
    failure_entry[0] = '\0';
    failure_status = 0;
    null_entry[0] = '\0';
    m3_callback = NULL;
    m3_callback_state = NULL;
    memset(async_runs, 0, sizeof(async_runs));
    async_run_count = 0;
    async_complete_count = 0;
    last_async_stream = NULL;
    last_async_input = NULL;
    last_async_name[0] = '\0';
}

EXPORT void fake_set_next_status(int status) { ATOMIC_EXCHANGE(next_status, status); }
EXPORT void fake_set_create_null(int enabled) { ATOMIC_EXCHANGE(create_null, enabled); }
EXPORT int fake_target_destroy_count(void) { return target_destroy_count; }
EXPORT int fake_program_destroy_count(void) { return program_destroy_count; }
EXPORT int fake_target_assign_count(void) { return target_assign_count; }
EXPORT int fake_program_assign_count(void) { return program_assign_count; }
EXPORT int fake_target_live_count(void) { return target_live_count; }
EXPORT int fake_program_live_count(void) { return program_live_count; }
EXPORT int fake_target_assign_copied(void) { return target_assign_copied; }
EXPORT int fake_program_assign_copied(void) { return program_assign_copied; }
EXPORT int fake_sizeof_status(void) { return (int)sizeof(migraphx_status); }
EXPORT int fake_sizeof_bool(void) { return (int)sizeof(uint8_t); }
EXPORT int fake_sizeof_shape_type(void) { return (int)sizeof(int); }
EXPORT int fake_sizeof_target_handle(void) { return (int)sizeof(migraphx_target_t); }
EXPORT const char* fake_last_target_name(void) { return last_target_name; }
EXPORT const char* fake_last_model_path(void) { return last_model_path; }
EXPORT int fake_last_model_size(void) { return last_model_size; }
EXPORT int fake_parse_file_count(void) { return parse_file_count; }
EXPORT int fake_parse_buffer_count(void) { return parse_buffer_count; }
EXPORT int fake_compile_count(void) { return compile_count; }
EXPORT int fake_run_count(void) { return run_count; }
EXPORT int fake_last_parameter_count(void) { return last_parameter_count; }
EXPORT int64_t fake_last_default_loop_iterations(void) { return last_default_loop_iterations; }
EXPORT int64_t fake_last_limit_loop_iterations(void) { return last_limit_loop_iterations; }
EXPORT int fake_last_fast_math(void) { return last_fast_math; }
EXPORT int fake_last_exhaustive_tune(void) { return last_exhaustive_tune; }
EXPORT const char* fake_last_external_data_path(void) { return last_external_data_path; }
EXPORT int fake_m2_destroy_count(void) { return m2_destroy_count; }
EXPORT int fake_m2_live_count(void) { return m2_live_count; }
EXPORT int fake_m5_live_count(void) { return m5_live_count; }
EXPORT int fake_m5_destroy_count(void) { return m5_destroy_count; }
EXPORT int fake_async_run_count(void) { return async_run_count; }
EXPORT int fake_async_complete_count(void) { return async_complete_count; }
EXPORT void* fake_last_async_stream(void) { return last_async_stream; }
EXPORT void* fake_last_async_input(void) { return last_async_input; }
EXPORT const char* fake_last_async_name(void) { return last_async_name; }
EXPORT void fake_complete_stream(void* stream)
{
    size_t index;
    for(index = 0; index < sizeof(async_runs) / sizeof(async_runs[0]); ++index)
    {
        migraphx_arguments_t run = async_runs[index];
        if(run != NULL && run->pending && run->stream == stream)
        {
            memcpy(run->argument.buffer, run->queued_source, run->argument.shape.bytes);
            run->pending = 0;
            ATOMIC_INCREMENT(async_complete_count);
        }
    }
}
EXPORT void fake_complete_all_streams(void)
{
    size_t index;
    for(index = 0; index < sizeof(async_runs) / sizeof(async_runs[0]); ++index)
        if(async_runs[index] != NULL && async_runs[index]->pending) fake_complete_stream(async_runs[index]->stream);
}
EXPORT void fake_set_shape_mode(int value) { ATOMIC_EXCHANGE(shape_mode, value); }
EXPORT void fake_set_shape_type(int value) { ATOMIC_EXCHANGE(shape_type_override, value); }
EXPORT void fake_set_failure(const char* entry, int status)
{
    copy_string(failure_entry, sizeof(failure_entry), entry);
    ATOMIC_EXCHANGE(failure_status, status);
}
EXPORT void fake_set_null_output(const char* entry) { copy_string(null_entry, sizeof(null_entry), entry); }

EXPORT migraphx_status fake_m3_store_callback(fake_m3_callback callback, void* state)
{
    if(callback == NULL)
        return migraphx_status_bad_param;
    m3_callback = callback;
    m3_callback_state = state;
    return migraphx_status_success;
}

EXPORT migraphx_status fake_m3_invoke_stored(
    const char* text,
    size_t text_size,
    uint8_t flag,
    const void* borrowed,
    void** out_handle)
{
    migraphx_status status;
    if(m3_callback == NULL || text == NULL || borrowed == NULL || out_handle == NULL)
        return migraphx_status_bad_param;
    *out_handle = NULL;
    status = m3_callback(m3_callback_state, text, text_size, flag, borrowed, out_handle);
    if(status != migraphx_status_success)
        *out_handle = NULL;
    return status;
}

EXPORT void fake_m3_clear_callback(void)
{
    m3_callback = NULL;
    m3_callback_state = NULL;
}

EXPORT migraphx_status fake_m3_sum_size_t(const size_t* values, size_t count, size_t* out)
{
    size_t index;
    size_t sum = 0;
    if(values == NULL || out == NULL)
        return migraphx_status_bad_param;
    for(index = 0; index < count; ++index)
        sum += values[index];
    *out = sum;
    return migraphx_status_success;
}

EXPORT migraphx_status migraphx_target_destroy(migraphx_target_t target)
{
    if(target != NULL)
    {
        free(target);
        ATOMIC_INCREMENT(target_destroy_count);
        ATOMIC_DECREMENT(target_live_count);
    }
    return (migraphx_status)take_status_for("migraphx_target_destroy");
}

EXPORT migraphx_status migraphx_target_assign_to(migraphx_target_t output, const migraphx_target_t input)
{
    ATOMIC_INCREMENT(target_assign_count);
    if(output == NULL || input == NULL)
        return migraphx_status_bad_param;
    output->value = input->value;
    target_assign_copied = output->value == input->value;
    return (migraphx_status)take_status_for("migraphx_target_assign_to");
}

EXPORT migraphx_status migraphx_target_create(migraphx_target_t* target, const char* name)
{
    int status;
    size_t name_length;
    if(target == NULL || name == NULL)
        return migraphx_status_bad_param;
    name_length = strlen(name);
    if(name_length >= sizeof(last_target_name))
        name_length = sizeof(last_target_name) - 1;
    lock_target_name();
    memcpy(last_target_name, name, name_length);
    last_target_name[name_length] = '\0';
    unlock_target_name();
    if(take_null_for("migraphx_target_create"))
    {
        *target = NULL;
    }
    else
    {
        *target = (migraphx_target_t)malloc(sizeof(**target));
        if(*target == NULL)
            return migraphx_status_unknown_error;
        (*target)->value = ATOMIC_INCREMENT(next_value);
        ATOMIC_INCREMENT(target_live_count);
    }
    status = take_status_for("migraphx_target_create");
    return (migraphx_status)status;
}

EXPORT migraphx_status migraphx_program_destroy(migraphx_program_t program)
{
    if(program != NULL)
    {
        free(program);
        ATOMIC_INCREMENT(program_destroy_count);
        ATOMIC_DECREMENT(program_live_count);
    }
    return (migraphx_status)take_status_for("migraphx_program_destroy");
}

EXPORT migraphx_status migraphx_program_assign_to(migraphx_program_t output, const migraphx_program_t input)
{
    ATOMIC_INCREMENT(program_assign_count);
    if(output == NULL || input == NULL)
        return migraphx_status_bad_param;
    output->value = input->value;
    program_assign_copied = output->value == input->value;
    return (migraphx_status)take_status_for("migraphx_program_assign_to");
}

EXPORT migraphx_status migraphx_program_create(migraphx_program_t* program)
{
    int status;
    if(program == NULL)
        return migraphx_status_bad_param;
    if(take_null_for("migraphx_program_create"))
    {
        *program = NULL;
    }
    else
    {
        *program = (migraphx_program_t)malloc(sizeof(**program));
        if(*program == NULL)
            return migraphx_status_unknown_error;
        (*program)->value = ATOMIC_INCREMENT(next_value);
        (*program)->compiled = 0;
        (*program)->dynamic = 0;
        (*program)->dynamic_count = 0;
        (*program)->static_count = 0;
        ATOMIC_INCREMENT(program_live_count);
    }
    status = take_status_for("migraphx_program_create");
    return (migraphx_status)status;
}

static void destroy_m2(void* value);
static void* create_m2(size_t size);
static size_t element_size(int type);

static void initialize_shape(fake_shape* shape, const migraphx_program_t program)
{
    int mode = shape_mode;
    shape->type = shape_type_override >= 0 ? shape_type_override : mode == 3 ? 10 : 4;
    shape->rank = program != NULL && program->static_count != 0 ? program->static_count : 2;
    shape->lengths[0] = 1;
    shape->lengths[1] = 4;
    if(program != NULL && program->static_count != 0)
    {
        for(size_t i = 0; i < program->static_count && i < 8; ++i) shape->lengths[i] = program->static_values[i];
        size_t running = 1;
        for(size_t i = program->static_count; i > 0; --i)
        {
            shape->strides[i - 1] = running;
            running *= shape->lengths[i - 1];
        }
        shape->elements = running;
    }
    else
    {
        shape->strides[0] = 4;
        shape->strides[1] = 1;
        shape->elements = 4;
    }
    if(mode == 13) shape->elements = (size_t)-1;
    shape->bytes = mode == 13 ? (size_t)-1 : shape->elements * element_size(shape->type);
    shape->standard = mode == 2 ? 0 : 1;
    shape->dynamic = (mode == 1 || (program != NULL && program->dynamic)) ? 1 : 0;
    shape->dynamic_count = shape->dynamic ? (program != NULL && program->dynamic_count != 0 ? program->dynamic_count : 2) : 0;
    if(shape->dynamic)
    {
        if(program != NULL && program->dynamic_count != 0)
            for(size_t i = 0; i < program->dynamic_count && i < 8; ++i) shape->dynamic_values[i] = program->dynamic_values[i];
        else
        {
            shape->dynamic_values[0].minimum = 1; shape->dynamic_values[0].maximum = 4;
            shape->dynamic_values[1].minimum = 1; shape->dynamic_values[1].maximum = 8;
        }
    }
}

static size_t element_size(int type)
{
    switch(type)
    {
    case 6:
    case 7: return 1;
    case 8:
    case 9: return 2;
    case 4:
    case 10:
    case 12: return 4;
    case 5:
    case 11:
    case 13: return 8;
    default: return 0;
    }
}

EXPORT migraphx_status migraphx_shape_destroy(migraphx_shape_t value)
{
    destroy_m2(value);
    return (migraphx_status)take_status_for("migraphx_shape_destroy");
}

EXPORT migraphx_status migraphx_shape_create(migraphx_shape_t* out, int type, const size_t* lengths, size_t lengths_size)
{
    size_t index;
    int status;
    if(out == NULL || (lengths_size != 0 && lengths == NULL) || lengths_size > 8 || element_size(type) == 0)
        return migraphx_status_bad_param;
    if(take_null_for("migraphx_shape_create"))
    {
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_shape_create");
    }
    *out = (migraphx_shape_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->type = type;
    (*out)->rank = lengths_size;
    (*out)->elements = 1;
    (*out)->standard = 1;
    (*out)->dynamic = 0;
    for(index = 0; index < lengths_size; ++index)
    {
        (*out)->lengths[index] = lengths[index];
        (*out)->elements *= lengths[index];
    }
    {
        size_t stride = 1;
        for(index = lengths_size; index > 0; --index)
        {
            (*out)->strides[index - 1] = stride;
            stride *= (*out)->lengths[index - 1];
        }
    }
    (*out)->bytes = (*out)->elements * element_size(type);
    status = take_status_for("migraphx_shape_create");
    return (migraphx_status)status;
}

static void destroy_m2(void* value)
{
    if(value != NULL)
    {
        free(value);
        ATOMIC_INCREMENT(m2_destroy_count);
        ATOMIC_DECREMENT(m2_live_count);
    }
}

static void* create_m2(size_t size)
{
    void* value = calloc(1, size);
    if(value != NULL)
        ATOMIC_INCREMENT(m2_live_count);
    return value;
}

EXPORT migraphx_status migraphx_program_parameter_shapes_destroy(migraphx_program_parameter_shapes_t value) { destroy_m2(value); return (migraphx_status)take_status_for("migraphx_program_parameter_shapes_destroy"); }
EXPORT migraphx_status migraphx_program_parameter_shapes_size(size_t* out, migraphx_program_parameter_shapes_t value)
{
    if(out == NULL || value == NULL) return migraphx_status_bad_param;
    if(shape_mode == 10)
        *out = (ATOMIC_INCREMENT(parameter_size_reads) % 2) == 1 ? 1 : 2;
    else
        *out = shape_mode == 4 || shape_mode == 11 ? 2 : 1;
    return (migraphx_status)take_status_for("migraphx_program_parameter_shapes_size");
}
EXPORT migraphx_status migraphx_program_parameter_shapes_get(const fake_shape** out, migraphx_program_parameter_shapes_t value, const char* name)
{
    if(out == NULL || value == NULL || name == NULL || (strcmp(name, "input") != 0 && strcmp(name, "second") != 0)) return migraphx_status_bad_param;
    *out = shape_mode == 7 ? NULL : &value->shape;
    return (migraphx_status)take_status_for("migraphx_program_parameter_shapes_get");
}
EXPORT migraphx_status migraphx_program_parameter_shapes_names(const char** out, migraphx_program_parameter_shapes_t value)
{
    static const char input_name[] = "input";
    if(out == NULL || value == NULL) return migraphx_status_bad_param;
    out[0] = shape_mode == 12 ? NULL : input_name;
    if(shape_mode == 4 || shape_mode == 11) out[1] = shape_mode == 11 ? input_name : "second";
    return (migraphx_status)take_status_for("migraphx_program_parameter_shapes_names");
}
EXPORT migraphx_status migraphx_program_parameters_destroy(migraphx_program_parameters_t value) { destroy_m2(value); return (migraphx_status)take_status_for("migraphx_program_parameters_destroy"); }
EXPORT migraphx_status migraphx_program_parameters_create(migraphx_program_parameters_t* out)
{
    int status;
    if(out == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_program_parameters_create")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_program_parameters_create"); }
    *out = (migraphx_program_parameters_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    status = take_status_for("migraphx_program_parameters_create");
    return (migraphx_status)status;
}
EXPORT migraphx_status migraphx_program_parameters_add(migraphx_program_parameters_t value, const char* name, const migraphx_argument_t argument)
{
    if(value == NULL || name == NULL || argument == NULL) return migraphx_status_bad_param;
    if(value->count >= 4) return migraphx_status_bad_param;
    value->arguments[value->count] = *argument;
    value->arguments[value->count].owns_buffer = 0;
    copy_string(value->names[value->count], sizeof(value->names[value->count]), name);
    value->count += 1;
    ATOMIC_EXCHANGE(last_parameter_count, (int32_t)value->count);
    return (migraphx_status)take_status_for("migraphx_program_parameters_add");
}
EXPORT migraphx_status migraphx_arguments_destroy(migraphx_arguments_t value)
{
    size_t index;
    for(index = 0; index < sizeof(async_runs) / sizeof(async_runs[0]); ++index)
        if(async_runs[index] == value) async_runs[index] = NULL;
    if(value != NULL && value->argument.owns_buffer) free(value->argument.buffer);
    destroy_m2(value);
    return (migraphx_status)take_status_for("migraphx_arguments_destroy");
}
EXPORT migraphx_status migraphx_arguments_size(size_t* out, migraphx_arguments_t value)
{
    if(out == NULL || value == NULL) return migraphx_status_bad_param;
    if(shape_mode == 16)
        *out = (ATOMIC_INCREMENT(argument_size_reads) % 2) == 1 ? 1 : 2;
    else
        *out = shape_mode == 6 ? 2 : 1;
    return (migraphx_status)take_status_for("migraphx_arguments_size");
}
EXPORT migraphx_status migraphx_arguments_get(const struct fake_argument** out, migraphx_arguments_t value, size_t idx)
{
    if(out == NULL || value == NULL || idx >= (shape_mode == 6 ? 2u : 1u)) return migraphx_status_bad_param;
    if(value->pending) return migraphx_status_unknown_error;
    *out = shape_mode == 9 ? NULL : &value->argument;
    return (migraphx_status)take_status_for("migraphx_arguments_get");
}
EXPORT migraphx_status migraphx_shapes_destroy(migraphx_shapes_t value) { destroy_m2(value); return (migraphx_status)take_status_for("migraphx_shapes_destroy"); }
EXPORT migraphx_status migraphx_shapes_size(size_t* out, migraphx_shapes_t value)
{
    if(out == NULL || value == NULL) return migraphx_status_bad_param;
    *out = shape_mode == 5 ? 2 : 1;
    return (migraphx_status)take_status_for("migraphx_shapes_size");
}
EXPORT migraphx_status migraphx_shapes_get(const fake_shape** out, migraphx_shapes_t value, size_t idx)
{
    if(out == NULL || value == NULL || idx >= (shape_mode == 5 ? 2u : 1u)) return migraphx_status_bad_param;
    *out = shape_mode == 8 ? NULL : &value->shape;
    return (migraphx_status)take_status_for("migraphx_shapes_get");
}
EXPORT migraphx_status migraphx_shape_lengths(const size_t** out, size_t* out_size, const fake_shape* shape)
{
    if(out == NULL || out_size == NULL || shape == NULL) return migraphx_status_bad_param;
    *out = shape->lengths; *out_size = shape->rank; return (migraphx_status)take_status_for("migraphx_shape_lengths");
}
EXPORT migraphx_status migraphx_shape_strides(const size_t** out, size_t* out_size, const fake_shape* shape)
{
    if(out == NULL || out_size == NULL || shape == NULL) return migraphx_status_bad_param;
    *out = shape->strides; *out_size = shape_mode == 14 ? shape->rank - 1 : shape->rank; return (migraphx_status)take_status_for("migraphx_shape_strides");
}
EXPORT migraphx_status migraphx_shape_type(int* out, const fake_shape* shape) { if(out == NULL || shape == NULL) return migraphx_status_bad_param; *out = shape->type; return (migraphx_status)take_status_for("migraphx_shape_type"); }
EXPORT migraphx_status migraphx_shape_bytes(size_t* out, const fake_shape* shape) { if(out == NULL || shape == NULL) return migraphx_status_bad_param; *out = shape->bytes; return (migraphx_status)take_status_for("migraphx_shape_bytes"); }
EXPORT migraphx_status migraphx_shape_elements(size_t* out, const fake_shape* shape) { if(out == NULL || shape == NULL) return migraphx_status_bad_param; *out = shape->elements; return (migraphx_status)take_status_for("migraphx_shape_elements"); }
EXPORT migraphx_status migraphx_shape_standard(uint8_t* out, const fake_shape* shape) { if(out == NULL || shape == NULL) return migraphx_status_bad_param; *out = shape->standard; return (migraphx_status)take_status_for("migraphx_shape_standard"); }
EXPORT migraphx_status migraphx_shape_dynamic(uint8_t* out, const fake_shape* shape) { if(out == NULL || shape == NULL) return migraphx_status_bad_param; *out = shape->dynamic; return (migraphx_status)take_status_for("migraphx_shape_dynamic"); }
EXPORT migraphx_status migraphx_argument_destroy(migraphx_argument_t value) { destroy_m2(value); return (migraphx_status)take_status_for("migraphx_argument_destroy"); }
EXPORT migraphx_status migraphx_argument_create(migraphx_argument_t* out, const fake_shape* shape, void* buffer)
{
    int status;
    if(out == NULL || shape == NULL || buffer == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_argument_create")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_argument_create"); }
    *out = (migraphx_argument_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->shape = *shape; (*out)->buffer = (char*)buffer; (*out)->owns_buffer = 0;
    status = take_status_for("migraphx_argument_create");
    return (migraphx_status)status;
}
EXPORT migraphx_status migraphx_argument_shape(const fake_shape** out, const migraphx_argument_t value) { if(out == NULL || value == NULL) return migraphx_status_bad_param; *out = &value->shape; return (migraphx_status)take_status_for("migraphx_argument_shape"); }
EXPORT migraphx_status migraphx_argument_buffer(char** out, const migraphx_argument_t value) { if(out == NULL || value == NULL) return migraphx_status_bad_param; *out = value->buffer; return (migraphx_status)take_status_for("migraphx_argument_buffer"); }
EXPORT migraphx_status migraphx_program_compile(migraphx_program_t program, migraphx_target_t target, migraphx_compile_options_t options)
{
    if(program == NULL || target == NULL || options == NULL) return migraphx_status_bad_param;
    program->compiled = 1; program->offload_copy = options->offload_copy != 0; ATOMIC_INCREMENT(compile_count); return (migraphx_status)take_status_for("migraphx_program_compile");
}
EXPORT migraphx_status migraphx_program_get_parameter_shapes(migraphx_program_parameter_shapes_t* out, migraphx_program_t program)
{
    int status;
    if(out == NULL || program == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_program_get_parameter_shapes")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_program_get_parameter_shapes"); }
    *out = (migraphx_program_parameter_shapes_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    initialize_shape(&(*out)->shape, program); status = take_status_for("migraphx_program_get_parameter_shapes"); return (migraphx_status)status;
}
EXPORT migraphx_status migraphx_program_get_output_shapes(migraphx_shapes_t* out, migraphx_program_t program)
{
    int status;
    if(out == NULL || program == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_program_get_output_shapes")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_program_get_output_shapes"); }
    *out = (migraphx_shapes_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    initialize_shape(&(*out)->shape, program); status = take_status_for("migraphx_program_get_output_shapes"); return (migraphx_status)status;
}
EXPORT migraphx_status migraphx_program_run(migraphx_arguments_t* out, migraphx_program_t program, migraphx_program_parameters_t parameters)
{
    int status;
    if(out == NULL || program == NULL || parameters == NULL || !program->compiled || parameters->count == 0) return migraphx_status_bad_param;
    if(take_null_for("migraphx_program_run")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_program_run"); }
    *out = (migraphx_arguments_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->argument.shape = parameters->arguments[0].shape;
    (*out)->argument.buffer = (char*)malloc((*out)->argument.shape.bytes);
    if((*out)->argument.buffer == NULL) { destroy_m2(*out); *out = NULL; return migraphx_status_unknown_error; }
    memcpy((*out)->argument.buffer, parameters->arguments[0].buffer, (*out)->argument.shape.bytes);
    (*out)->argument.owns_buffer = 1;
    ATOMIC_INCREMENT(run_count); status = take_status_for("migraphx_program_run"); return (migraphx_status)status;
}
EXPORT migraphx_status migraphx_program_run_async(
    migraphx_arguments_t* out,
    migraphx_program_t program,
    migraphx_program_parameters_t parameters,
    void* stream,
    const char* name)
{
    size_t slot;
    int status;
    if(out == NULL || program == NULL || parameters == NULL || stream == NULL || name == NULL ||
       strcmp(name, "hipStream_t") != 0 || !program->compiled || parameters->count == 0)
        return migraphx_status_bad_param;
    if(take_null_for("migraphx_program_run_async")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_program_run_async"); }
    *out = (migraphx_arguments_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->argument.shape = parameters->arguments[0].shape;
    (*out)->argument.buffer = (char*)malloc((*out)->argument.shape.bytes);
    if((*out)->argument.buffer == NULL) { destroy_m2(*out); *out = NULL; return migraphx_status_unknown_error; }
    memset((*out)->argument.buffer, 0, (*out)->argument.shape.bytes);
    (*out)->argument.owns_buffer = 1;
    (*out)->stream = stream;
    (*out)->queued_source = parameters->arguments[0].buffer;
    (*out)->pending = 1;
    for(slot = 0; slot < sizeof(async_runs) / sizeof(async_runs[0]); ++slot)
        if(async_runs[slot] == NULL) { async_runs[slot] = *out; break; }
    if(slot == sizeof(async_runs) / sizeof(async_runs[0]))
    {
        free((*out)->argument.buffer); destroy_m2(*out); *out = NULL; return migraphx_status_unknown_error;
    }
    last_async_stream = stream;
    last_async_input = parameters->arguments[0].buffer;
    copy_string(last_async_name, sizeof(last_async_name), name);
    ATOMIC_INCREMENT(async_run_count);
    ATOMIC_INCREMENT(run_count);
    status = take_status_for("migraphx_program_run_async");
    return (migraphx_status)status;
}
EXPORT migraphx_status migraphx_onnx_options_destroy(migraphx_onnx_options_t value) { destroy_m2(value); return (migraphx_status)take_status_for("migraphx_onnx_options_destroy"); }
EXPORT migraphx_status migraphx_onnx_options_create(migraphx_onnx_options_t* out)
{
    int status; if(out == NULL) return migraphx_status_bad_param; if(take_null_for("migraphx_onnx_options_create")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_onnx_options_create"); } *out = (migraphx_onnx_options_t)create_m2(sizeof(**out)); if(*out == NULL) return migraphx_status_unknown_error; status = take_status_for("migraphx_onnx_options_create"); return (migraphx_status)status;
}
EXPORT migraphx_status migraphx_compile_options_destroy(migraphx_compile_options_t value) { destroy_m2(value); return (migraphx_status)take_status_for("migraphx_compile_options_destroy"); }
EXPORT migraphx_status migraphx_compile_options_create(migraphx_compile_options_t* out)
{
    int status; if(out == NULL) return migraphx_status_bad_param; if(take_null_for("migraphx_compile_options_create")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_compile_options_create"); } *out = (migraphx_compile_options_t)create_m2(sizeof(**out)); if(*out == NULL) return migraphx_status_unknown_error; status = take_status_for("migraphx_compile_options_create"); return (migraphx_status)status;
}
EXPORT migraphx_status migraphx_compile_options_set_offload_copy(migraphx_compile_options_t value, uint8_t enabled) { if(value == NULL) return migraphx_status_bad_param; value->offload_copy = enabled; return (migraphx_status)take_status_for("migraphx_compile_options_set_offload_copy"); }
EXPORT migraphx_status migraphx_compile_options_set_fast_math(migraphx_compile_options_t value, uint8_t enabled) { if(value == NULL) return migraphx_status_bad_param; value->fast_math = enabled; last_fast_math = enabled; return (migraphx_status)take_status_for("migraphx_compile_options_set_fast_math"); }
EXPORT migraphx_status migraphx_compile_options_set_exhaustive_tune_flag(migraphx_compile_options_t value, uint8_t enabled) { if(value == NULL) return migraphx_status_bad_param; value->exhaustive_tune = enabled; last_exhaustive_tune = enabled; return (migraphx_status)take_status_for("migraphx_compile_options_set_exhaustive_tune_flag"); }
EXPORT migraphx_status migraphx_parse_onnx(migraphx_program_t* out, const char* name, migraphx_onnx_options_t options)
{
    migraphx_status status;
    if(out == NULL || name == NULL || options == NULL) return migraphx_status_bad_param;
    status = migraphx_program_create(out);
    if(status == migraphx_status_success && *out != NULL && options != NULL)
    {
        (*out)->dynamic = options->dynamic;
        (*out)->dynamic_count = options->dynamic_count;
        memcpy((*out)->dynamic_values, options->dynamic_values, sizeof(options->dynamic_values));
        (*out)->static_count = options->static_count;
        memcpy((*out)->static_values, options->static_values, sizeof(options->static_values));
    }
    if(status == migraphx_status_success && take_null_for("migraphx_parse_onnx")) { migraphx_program_destroy(*out); *out = NULL; }
    copy_string(last_model_path, sizeof(last_model_path), name);
    ATOMIC_INCREMENT(parse_file_count);
    if(status != migraphx_status_success) return status;
    return (migraphx_status)take_status_for("migraphx_parse_onnx");
}
EXPORT migraphx_status migraphx_parse_onnx_buffer(migraphx_program_t* out, const void* data, size_t size, migraphx_onnx_options_t options)
{
    migraphx_status status;
    if(out == NULL || data == NULL || size == 0 || options == NULL) return migraphx_status_bad_param;
    status = migraphx_program_create(out);
    if(status == migraphx_status_success && *out != NULL && options != NULL)
    {
        (*out)->dynamic = options->dynamic;
        (*out)->dynamic_count = options->dynamic_count;
        memcpy((*out)->dynamic_values, options->dynamic_values, sizeof(options->dynamic_values));
        (*out)->static_count = options->static_count;
        memcpy((*out)->static_values, options->static_values, sizeof(options->static_values));
    }
    if(status == migraphx_status_success && take_null_for("migraphx_parse_onnx_buffer")) { migraphx_program_destroy(*out); *out = NULL; }
    ATOMIC_EXCHANGE(last_model_size, (int)size);
    ATOMIC_INCREMENT(parse_buffer_count);
    if(status != migraphx_status_success) return status;
    return (migraphx_status)take_status_for("migraphx_parse_onnx_buffer");
}

static void* create_m5(size_t size)
{
    void* value = calloc(1, size);
    if(value != NULL) ATOMIC_INCREMENT(m5_live_count);
    return value;
}

static void destroy_m5(void* value)
{
    if(value != NULL) { free(value); ATOMIC_INCREMENT(m5_destroy_count); ATOMIC_DECREMENT(m5_live_count); }
}

EXPORT migraphx_status migraphx_optimals_destroy(migraphx_optimals_t value) { destroy_m5(value); return (migraphx_status)take_status_for("migraphx_optimals_destroy"); }
EXPORT migraphx_status migraphx_optimals_create(migraphx_optimals_t* out, const size_t* values, size_t count)
{
    if(out == NULL || (count != 0 && values == NULL) || count > 8) return migraphx_status_bad_param;
    if(take_null_for("migraphx_optimals_create")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_optimals_create"); }
    *out = (migraphx_optimals_t)create_m5(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->count = count; if(count != 0) memcpy((*out)->values, values, count * sizeof(size_t));
    return (migraphx_status)take_status_for("migraphx_optimals_create");
}

EXPORT migraphx_status migraphx_dynamic_dimension_destroy(migraphx_dynamic_dimension_t value) { destroy_m5(value); return (migraphx_status)take_status_for("migraphx_dynamic_dimension_destroy"); }
EXPORT migraphx_status migraphx_dynamic_dimension_create_min_max(migraphx_dynamic_dimension_t* out, size_t minimum, size_t maximum)
{
    if(out == NULL || minimum > maximum) return migraphx_status_bad_param;
    if(take_null_for("migraphx_dynamic_dimension_create_min_max")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_dynamic_dimension_create_min_max"); }
    *out = (migraphx_dynamic_dimension_t)create_m5(sizeof(**out)); if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->minimum = minimum; (*out)->maximum = maximum;
    return (migraphx_status)take_status_for("migraphx_dynamic_dimension_create_min_max");
}
EXPORT migraphx_status migraphx_dynamic_dimension_create_min_max_optimals(migraphx_dynamic_dimension_t* out, size_t minimum, size_t maximum, migraphx_optimals_t optimals)
{
    if(out == NULL || optimals == NULL || minimum > maximum || optimals->count > 8) return migraphx_status_bad_param;
    if(take_null_for("migraphx_dynamic_dimension_create_min_max_optimals")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_dynamic_dimension_create_min_max_optimals"); }
    *out = (migraphx_dynamic_dimension_t)create_m5(sizeof(**out)); if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->minimum = minimum; (*out)->maximum = maximum; (*out)->optimal_count = optimals->count; memcpy((*out)->optimals, optimals->values, optimals->count * sizeof(size_t));
    return (migraphx_status)take_status_for("migraphx_dynamic_dimension_create_min_max_optimals");
}
EXPORT migraphx_status migraphx_dynamic_dimension_is_fixed(uint8_t* out, const_migraphx_dynamic_dimension_t value)
{
    if(out == NULL || value == NULL) return migraphx_status_bad_param; *out = value->minimum == value->maximum ? 1 : 0; return (migraphx_status)take_status_for("migraphx_dynamic_dimension_is_fixed");
}
EXPORT migraphx_status migraphx_dynamic_dimension_equal(uint8_t* out, const_migraphx_dynamic_dimension_t left, const_migraphx_dynamic_dimension_t right)
{
    if(out == NULL || left == NULL || right == NULL) return migraphx_status_bad_param;
    *out = left->minimum == right->minimum && left->maximum == right->maximum && left->optimal_count == right->optimal_count && memcmp(left->optimals, right->optimals, left->optimal_count * sizeof(size_t)) == 0;
    return (migraphx_status)take_status_for("migraphx_dynamic_dimension_equal");
}
EXPORT migraphx_status migraphx_dynamic_dimensions_destroy(migraphx_dynamic_dimensions_t value) { destroy_m5(value); return (migraphx_status)take_status_for("migraphx_dynamic_dimensions_destroy"); }
EXPORT migraphx_status migraphx_dynamic_dimensions_create(migraphx_dynamic_dimensions_t* out, const migraphx_dynamic_dimension_t* values, size_t count)
{
    if(out == NULL || (count != 0 && values == NULL) || count > 8) return migraphx_status_bad_param;
    if(take_null_for("migraphx_dynamic_dimensions_create")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_dynamic_dimensions_create"); }
    *out = (migraphx_dynamic_dimensions_t)create_m5(sizeof(**out)); if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->count = count; for(size_t i = 0; i < count; ++i) { if(values[i] == NULL) { destroy_m5(*out); *out = NULL; return migraphx_status_bad_param; } (*out)->values[i] = values[i]; }
    return (migraphx_status)take_status_for("migraphx_dynamic_dimensions_create");
}
EXPORT migraphx_status migraphx_dynamic_dimensions_size(size_t* out, migraphx_dynamic_dimensions_t value)
{
    if(out == NULL || value == NULL) return migraphx_status_bad_param;
    *out = shape_mode == 15 && value->size_calls++ != 0 ? value->count + 1 : value->count;
    return (migraphx_status)take_status_for("migraphx_dynamic_dimensions_size");
}
EXPORT migraphx_status migraphx_dynamic_dimensions_get(migraphx_dynamic_dimension_t* out, migraphx_dynamic_dimensions_t value, size_t index)
{
    if(out == NULL || value == NULL || index >= value->count) return migraphx_status_bad_param;
    if(take_null_for("migraphx_dynamic_dimensions_get")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_dynamic_dimensions_get"); }
    *out = value->values[index]; return (migraphx_status)take_status_for("migraphx_dynamic_dimensions_get");
}

EXPORT migraphx_status migraphx_shape_create_dynamic(migraphx_shape_t* out, int type, migraphx_dynamic_dimensions_t dims)
{
    if(out == NULL || dims == NULL || dims->count > 8 || element_size(type) == 0) return migraphx_status_bad_param;
    if(take_null_for("migraphx_shape_create_dynamic")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_shape_create_dynamic"); }
    *out = (migraphx_shape_t)create_m2(sizeof(**out)); if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->type = type; (*out)->rank = dims->count; (*out)->dynamic = 1; (*out)->standard = 0; (*out)->dynamic_count = dims->count; (*out)->elements = 0; (*out)->bytes = 0;
    for(size_t i = 0; i < dims->count; ++i) { (*out)->dynamic_values[i] = *dims->values[i]; (*out)->lengths[i] = dims->values[i]->minimum; (*out)->strides[i] = 1; }
    return (migraphx_status)take_status_for("migraphx_shape_create_dynamic");
}
EXPORT migraphx_status migraphx_shape_dyn_dims(migraphx_dynamic_dimensions_t* out, migraphx_shape_t shape)
{
    if(out == NULL || shape == NULL || !shape->dynamic) return migraphx_status_bad_param;
    if(take_null_for("migraphx_shape_dyn_dims")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_shape_dyn_dims"); }
    *out = (migraphx_dynamic_dimensions_t)create_m5(sizeof(**out)); if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->count = shape->dynamic_count; for(size_t i = 0; i < shape->dynamic_count; ++i) (*out)->values[i] = &shape->dynamic_values[i];
    return (migraphx_status)take_status_for("migraphx_shape_dyn_dims");
}

EXPORT migraphx_status migraphx_onnx_options_set_input_parameter_shape(migraphx_onnx_options_t options, const char* name, const size_t* values, size_t count)
{
    if(options == NULL || name == NULL || (count != 0 && values == NULL) || count > 8) return migraphx_status_bad_param;
    options->static_override = 1; options->static_count = count; if(count != 0) memcpy(options->static_values, values, count * sizeof(size_t));
    return (migraphx_status)take_status_for("migraphx_onnx_options_set_input_parameter_shape");
}
EXPORT migraphx_status migraphx_onnx_options_set_dyn_input_parameter_shape(migraphx_onnx_options_t options, const char* name, migraphx_dynamic_dimensions_t dims)
{
    if(options == NULL || name == NULL || dims == NULL || dims->count > 8) return migraphx_status_bad_param;
    options->dynamic = 1; options->dynamic_count = dims->count; for(size_t i = 0; i < dims->count; ++i) options->dynamic_values[i] = *dims->values[i];
    return (migraphx_status)take_status_for("migraphx_onnx_options_set_dyn_input_parameter_shape");
}
EXPORT migraphx_status migraphx_onnx_options_set_default_dim_value(migraphx_onnx_options_t options, size_t value)
{
    (void)value;
    if(options == NULL) return migraphx_status_bad_param; return (migraphx_status)take_status_for("migraphx_onnx_options_set_default_dim_value");
}
EXPORT migraphx_status migraphx_onnx_options_set_default_dyn_dim_value(migraphx_onnx_options_t options, migraphx_dynamic_dimension_t value)
{
    if(options == NULL || value == NULL) return migraphx_status_bad_param; return (migraphx_status)take_status_for("migraphx_onnx_options_set_default_dyn_dim_value");
}
EXPORT migraphx_status migraphx_onnx_options_set_default_loop_iterations(migraphx_onnx_options_t options, int64_t value)
{
    if(options == NULL || value < 0) return migraphx_status_bad_param;
    options->default_loop_iterations = value;
    last_default_loop_iterations = value;
    return (migraphx_status)take_status_for("migraphx_onnx_options_set_default_loop_iterations");
}
EXPORT migraphx_status migraphx_onnx_options_set_limit_loop_iterations(migraphx_onnx_options_t options, int64_t value)
{
    if(options == NULL || value < 0) return migraphx_status_bad_param;
    options->limit_loop_iterations = value;
    last_limit_loop_iterations = value;
    return (migraphx_status)take_status_for("migraphx_onnx_options_set_limit_loop_iterations");
}
EXPORT migraphx_status migraphx_onnx_options_set_external_data_path(migraphx_onnx_options_t options, const char* path)
{
    if(options == NULL || path == NULL) return migraphx_status_bad_param;
    copy_string(options->external_data_path, sizeof(options->external_data_path), path);
    copy_string(last_external_data_path, sizeof(last_external_data_path), path);
    return (migraphx_status)take_status_for("migraphx_onnx_options_set_external_data_path");
}

EXPORT migraphx_status migraphx_file_options_destroy(migraphx_file_options_t value) { destroy_m5(value); return (migraphx_status)take_status_for("migraphx_file_options_destroy"); }
EXPORT migraphx_status migraphx_file_options_create(migraphx_file_options_t* out)
{
    if(out == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_file_options_create")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_file_options_create"); }
    *out = (migraphx_file_options_t)create_m5(sizeof(**out)); if(*out == NULL) return migraphx_status_unknown_error; return (migraphx_status)take_status_for("migraphx_file_options_create");
}
EXPORT migraphx_status migraphx_file_options_set_file_format(migraphx_file_options_t options, const char* format)
{
    if(options == NULL || format == NULL || strcmp(format, "msgpack") != 0) return migraphx_status_bad_param; copy_string(options->format, sizeof(options->format), format); return (migraphx_status)take_status_for("migraphx_file_options_set_file_format");
}
EXPORT migraphx_status migraphx_save(migraphx_program_t program, const char* name, migraphx_file_options_t options)
{
    if(program == NULL || name == NULL || options == NULL || strcmp(options->format, "msgpack") != 0) return migraphx_status_bad_param;
    FILE* file = fopen(name, "wb"); if(file == NULL) return migraphx_status_unknown_error; fputs("fake-migraphx-msgpack\n", file); fclose(file); return (migraphx_status)take_status_for("migraphx_save");
}
EXPORT migraphx_status migraphx_load(migraphx_program_t* out, const char* name, migraphx_file_options_t options)
{
    if(out == NULL || name == NULL || options == NULL || strcmp(options->format, "msgpack") != 0) return migraphx_status_bad_param;
    FILE* file = fopen(name, "rb"); if(file == NULL) return migraphx_status_unknown_error; fclose(file);
    if(take_null_for("migraphx_load")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_load"); }
    *out = (migraphx_program_t)malloc(sizeof(**out)); if(*out == NULL) return migraphx_status_unknown_error; memset(*out, 0, sizeof(**out)); (*out)->value = ATOMIC_INCREMENT(next_value); ATOMIC_INCREMENT(program_live_count); return (migraphx_status)take_status_for("migraphx_load");
}
