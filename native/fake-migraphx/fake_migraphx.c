#include <stdint.h>
#include <stdlib.h>
#include <string.h>

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
typedef struct fake_program { int value; int compiled; } *migraphx_program_t;
typedef struct fake_options { int value; } *migraphx_onnx_options_t;
typedef struct fake_compile_options { uint8_t offload_copy; } *migraphx_compile_options_t;
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
typedef struct fake_arguments { struct fake_argument argument; } *migraphx_arguments_t;
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
static volatile int32_t shape_mode;
static volatile int32_t shape_type_override = -1;
static volatile int32_t parameter_size_reads;
static volatile int32_t last_parameter_count;
static char failure_entry[128];
static volatile int32_t failure_status;
static char null_entry[128];
static fake_m3_callback m3_callback;
static void* m3_callback_state;

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
    shape_mode = 0;
    shape_type_override = -1;
    parameter_size_reads = 0;
    last_parameter_count = 0;
    failure_entry[0] = '\0';
    failure_status = 0;
    null_entry[0] = '\0';
    m3_callback = NULL;
    m3_callback_state = NULL;
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
EXPORT int fake_m2_destroy_count(void) { return m2_destroy_count; }
EXPORT int fake_m2_live_count(void) { return m2_live_count; }
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
        ATOMIC_INCREMENT(program_live_count);
    }
    status = take_status_for("migraphx_program_create");
    return (migraphx_status)status;
}

static void destroy_m2(void* value);
static void* create_m2(size_t size);
static size_t element_size(int type);

static void initialize_shape(fake_shape* shape)
{
    int mode = shape_mode;
    shape->type = shape_type_override >= 0 ? shape_type_override : mode == 3 ? 10 : 4;
    shape->rank = 2;
    shape->lengths[0] = 1;
    shape->lengths[1] = 4;
    shape->strides[0] = 4;
    shape->strides[1] = 1;
    shape->elements = mode == 13 ? (size_t)-1 : 4;
    shape->bytes = mode == 13 ? (size_t)-1 : shape->elements * element_size(shape->type);
    shape->standard = mode == 2 ? 0 : 1;
    shape->dynamic = mode == 1 ? 1 : 0;
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
    if(value != NULL && value->argument.owns_buffer) free(value->argument.buffer);
    destroy_m2(value);
    return (migraphx_status)take_status_for("migraphx_arguments_destroy");
}
EXPORT migraphx_status migraphx_arguments_size(size_t* out, migraphx_arguments_t value)
{
    if(out == NULL || value == NULL) return migraphx_status_bad_param;
    *out = shape_mode == 6 ? 2 : 1;
    return (migraphx_status)take_status_for("migraphx_arguments_size");
}
EXPORT migraphx_status migraphx_arguments_get(const struct fake_argument** out, migraphx_arguments_t value, size_t idx)
{
    if(out == NULL || value == NULL || idx >= (shape_mode == 6 ? 2u : 1u)) return migraphx_status_bad_param;
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
    if(program == NULL || target == NULL || options == NULL || !options->offload_copy) return migraphx_status_bad_param;
    program->compiled = 1; ATOMIC_INCREMENT(compile_count); return (migraphx_status)take_status_for("migraphx_program_compile");
}
EXPORT migraphx_status migraphx_program_get_parameter_shapes(migraphx_program_parameter_shapes_t* out, migraphx_program_t program)
{
    int status;
    if(out == NULL || program == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_program_get_parameter_shapes")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_program_get_parameter_shapes"); }
    *out = (migraphx_program_parameter_shapes_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    initialize_shape(&(*out)->shape); status = take_status_for("migraphx_program_get_parameter_shapes"); return (migraphx_status)status;
}
EXPORT migraphx_status migraphx_program_get_output_shapes(migraphx_shapes_t* out, migraphx_program_t program)
{
    int status;
    if(out == NULL || program == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_program_get_output_shapes")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_program_get_output_shapes"); }
    *out = (migraphx_shapes_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    initialize_shape(&(*out)->shape); status = take_status_for("migraphx_program_get_output_shapes"); return (migraphx_status)status;
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
EXPORT migraphx_status migraphx_parse_onnx(migraphx_program_t* out, const char* name, migraphx_onnx_options_t options)
{
    migraphx_status status;
    if(out == NULL || name == NULL || options == NULL) return migraphx_status_bad_param;
    status = migraphx_program_create(out);
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
    if(status == migraphx_status_success && take_null_for("migraphx_parse_onnx_buffer")) { migraphx_program_destroy(*out); *out = NULL; }
    ATOMIC_EXCHANGE(last_model_size, (int)size);
    ATOMIC_INCREMENT(parse_buffer_count);
    if(status != migraphx_status_success) return status;
    return (migraphx_status)take_status_for("migraphx_parse_onnx_buffer");
}
