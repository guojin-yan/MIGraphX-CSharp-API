/* M12 local-only fake implementations. Included by fake_migraphx.c. */

static void* create_m12(size_t size)
{
    void* value = calloc(1, size);
    if(value != NULL) ATOMIC_INCREMENT(m12_live_count);
    return value;
}

static void destroy_m12(void* value)
{
    if(value != NULL)
    {
        free(value);
        ATOMIC_INCREMENT(m12_destroy_count);
        ATOMIC_DECREMENT(m12_live_count);
    }
}

static FILE* open_m12_file(const char* filename, const char* mode)
{
#if defined(_MSC_VER)
    FILE* file = NULL;
    if(fopen_s(&file, filename, mode) != 0) return NULL;
    return file;
#else
    return fopen(filename, mode);
#endif
}

EXPORT migraphx_status migraphx_optimals_assign_to(migraphx_optimals_t output, const migraphx_optimals_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_optimals_assign_to");
}

EXPORT migraphx_status migraphx_dynamic_dimension_assign_to(migraphx_dynamic_dimension_t output, const_migraphx_dynamic_dimension_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_dynamic_dimension_assign_to");
}

EXPORT migraphx_status migraphx_dynamic_dimensions_assign_to(migraphx_dynamic_dimensions_t output, const migraphx_dynamic_dimensions_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_dynamic_dimensions_assign_to");
}

EXPORT migraphx_status migraphx_shape_assign_to(migraphx_shape_t output, const migraphx_shape_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_shape_assign_to");
}

EXPORT migraphx_status migraphx_shape_create_with_strides(
    migraphx_shape_t* out,
    int type,
    const size_t* lengths,
    size_t lengths_size,
    const size_t* strides,
    size_t strides_size)
{
    size_t index;
    if(out == NULL || lengths_size != strides_size || lengths_size > 8
       || (lengths_size != 0 && (lengths == NULL || strides == NULL)) || element_size(type) == 0)
        return migraphx_status_bad_param;
    if(take_null_for("migraphx_shape_create_with_strides"))
    {
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_shape_create_with_strides");
    }
    *out = (migraphx_shape_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->type = type;
    (*out)->rank = lengths_size;
    (*out)->elements = 1;
    (*out)->standard = 1;
    for(index = 0; index < lengths_size; ++index)
    {
        (*out)->lengths[index] = lengths[index];
        (*out)->strides[index] = strides[index];
        (*out)->elements *= lengths[index];
    }
    {
        size_t expected = 1;
        for(index = lengths_size; index > 0; --index)
        {
            if((*out)->strides[index - 1] != expected) (*out)->standard = 0;
            expected *= (*out)->lengths[index - 1];
        }
    }
    (*out)->bytes = (*out)->elements * element_size(type);
    return (migraphx_status)take_status_for("migraphx_shape_create_with_strides");
}

EXPORT migraphx_status migraphx_shape_create_scalar(migraphx_shape_t* out, int type)
{
    if(out == NULL || element_size(type) == 0) return migraphx_status_bad_param;
    if(take_null_for("migraphx_shape_create_scalar"))
    {
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_shape_create_scalar");
    }
    *out = (migraphx_shape_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->type = type;
    (*out)->elements = 1;
    (*out)->bytes = element_size(type);
    (*out)->standard = 1;
    return (migraphx_status)take_status_for("migraphx_shape_create_scalar");
}

EXPORT migraphx_status migraphx_shape_ndim(size_t* out, const migraphx_shape_t shape)
{
    if(out == NULL || shape == NULL) return migraphx_status_bad_param;
    *out = shape->rank;
    return (migraphx_status)take_status_for("migraphx_shape_ndim");
}

EXPORT migraphx_status migraphx_shape_equal(uint8_t* out, const migraphx_shape_t left, const migraphx_shape_t right)
{
    if(out == NULL || left == NULL || right == NULL) return migraphx_status_bad_param;
    *out = (uint8_t)(memcmp(left, right, sizeof(*left)) == 0);
    return (migraphx_status)take_status_for("migraphx_shape_equal");
}

EXPORT migraphx_status migraphx_shape_index(size_t* out, const migraphx_shape_t shape, size_t index)
{
    if(out == NULL || shape == NULL || index >= shape->rank) return migraphx_status_bad_param;
    *out = shape->lengths[index];
    return (migraphx_status)take_status_for("migraphx_shape_index");
}

EXPORT migraphx_status migraphx_argument_assign_to(migraphx_argument_t output, const migraphx_argument_t input)
{
    char* copy;
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    copy = (char*)malloc(input->shape.bytes == 0 ? 1 : input->shape.bytes);
    if(copy == NULL) return migraphx_status_unknown_error;
    if(input->shape.bytes != 0) memcpy(copy, input->buffer, input->shape.bytes);
    if(output->owns_buffer) free(output->buffer);
    output->shape = input->shape;
    output->buffer = copy;
    output->owns_buffer = 1;
    return (migraphx_status)take_status_for("migraphx_argument_assign_to");
}

EXPORT migraphx_status migraphx_argument_create_empty(migraphx_argument_t* out, const migraphx_shape_t shape)
{
    if(out == NULL || shape == NULL || shape->dynamic) return migraphx_status_bad_param;
    if(take_null_for("migraphx_argument_create_empty"))
    {
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_argument_create_empty");
    }
    *out = (migraphx_argument_t)create_m2(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->shape = *shape;
    (*out)->buffer = (char*)calloc(1, shape->bytes == 0 ? 1 : shape->bytes);
    if((*out)->buffer == NULL) { destroy_m2(*out); *out = NULL; return migraphx_status_unknown_error; }
    (*out)->owns_buffer = 1;
    return (migraphx_status)take_status_for("migraphx_argument_create_empty");
}

EXPORT migraphx_status migraphx_argument_save(const migraphx_argument_t argument, const char* filename)
{
    FILE* file;
    if(argument == NULL || filename == NULL) return migraphx_status_bad_param;
    file = open_m12_file(filename, "wb");
    if(file == NULL) return migraphx_status_unknown_error;
    if(fwrite(&argument->shape, sizeof(argument->shape), 1, file) != 1
       || (argument->shape.bytes != 0 && fwrite(argument->buffer, argument->shape.bytes, 1, file) != 1))
    {
        fclose(file);
        return migraphx_status_unknown_error;
    }
    fclose(file);
    return (migraphx_status)take_status_for("migraphx_argument_save");
}

EXPORT migraphx_status migraphx_argument_load(migraphx_argument_t* out, const char* filename)
{
    FILE* file;
    if(out == NULL || filename == NULL) return migraphx_status_bad_param;
    file = open_m12_file(filename, "rb");
    if(file == NULL) return migraphx_status_unknown_error;
    if(take_null_for("migraphx_argument_load"))
    {
        fclose(file);
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_argument_load");
    }
    *out = (migraphx_argument_t)create_m2(sizeof(**out));
    if(*out == NULL) { fclose(file); return migraphx_status_unknown_error; }
    if(fread(&(*out)->shape, sizeof((*out)->shape), 1, file) != 1)
    {
        fclose(file); destroy_m2(*out); *out = NULL; return migraphx_status_unknown_error;
    }
    (*out)->buffer = (char*)malloc((*out)->shape.bytes == 0 ? 1 : (*out)->shape.bytes);
    if((*out)->buffer == NULL || ((*out)->shape.bytes != 0 && fread((*out)->buffer, (*out)->shape.bytes, 1, file) != 1))
    {
        fclose(file); if((*out)->buffer != NULL) free((*out)->buffer); destroy_m2(*out); *out = NULL; return migraphx_status_unknown_error;
    }
    fclose(file);
    (*out)->owns_buffer = 1;
    return (migraphx_status)take_status_for("migraphx_argument_load");
}

EXPORT migraphx_status migraphx_argument_generate(migraphx_argument_t* out, const migraphx_shape_t shape, size_t seed)
{
    size_t index;
    migraphx_status status;
    if(out == NULL || shape == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_argument_generate"))
    {
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_argument_generate");
    }
    status = migraphx_argument_create_empty(out, shape);
    if(status != migraphx_status_success || *out == NULL) return status;
    for(index = 0; index < shape->bytes; ++index) (*out)->buffer[index] = (char)((seed + index * 17u) & 0xffu);
    return (migraphx_status)take_status_for("migraphx_argument_generate");
}

EXPORT migraphx_status migraphx_program_parameter_shapes_assign_to(migraphx_program_parameter_shapes_t output, const migraphx_program_parameter_shapes_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_program_parameter_shapes_assign_to");
}

EXPORT migraphx_status migraphx_program_parameters_assign_to(migraphx_program_parameters_t output, const migraphx_program_parameters_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_program_parameters_assign_to");
}

EXPORT migraphx_status migraphx_arguments_assign_to(migraphx_arguments_t output, const migraphx_arguments_t input)
{
    char* copied_buffer;
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    copied_buffer = (char*)malloc(input->argument.shape.bytes == 0 ? 1 : input->argument.shape.bytes);
    if(copied_buffer == NULL) return migraphx_status_unknown_error;
    if(input->argument.shape.bytes != 0)
        memcpy(copied_buffer, input->argument.buffer, input->argument.shape.bytes);
    if(output->argument.owns_buffer) free(output->argument.buffer);
    *output = *input;
    output->argument.buffer = copied_buffer;
    output->argument.owns_buffer = 1;
    output->pending = 0;
    output->queued_source = NULL;
    return (migraphx_status)take_status_for("migraphx_arguments_assign_to");
}

EXPORT migraphx_status migraphx_shapes_assign_to(migraphx_shapes_t output, const migraphx_shapes_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_shapes_assign_to");
}

EXPORT migraphx_status migraphx_instruction_destroy(migraphx_instruction_t instruction)
{
    destroy_m12(instruction);
    return (migraphx_status)take_status_for("migraphx_instruction_destroy");
}

EXPORT migraphx_status migraphx_instruction_assign_to(migraphx_instruction_t output, const migraphx_instruction_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_instruction_assign_to");
}

EXPORT migraphx_status migraphx_instructions_destroy(migraphx_instructions_t instructions)
{
    destroy_m12(instructions);
    return (migraphx_status)take_status_for("migraphx_instructions_destroy");
}

EXPORT migraphx_status migraphx_instructions_create(migraphx_instructions_t* out, const migraphx_instruction_t* values, size_t count)
{
    if(out == NULL || count > 16 || (count != 0 && values == NULL)) return migraphx_status_bad_param;
    if(take_null_for("migraphx_instructions_create")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_instructions_create"); }
    *out = (migraphx_instructions_t)create_m12(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->count = count;
    if(count != 0) memcpy((*out)->values, values, count * sizeof(values[0]));
    return (migraphx_status)take_status_for("migraphx_instructions_create");
}

EXPORT migraphx_status migraphx_instructions_assign_to(migraphx_instructions_t output, const migraphx_instructions_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_instructions_assign_to");
}

EXPORT migraphx_status migraphx_modules_destroy(migraphx_modules_t modules)
{
    destroy_m12(modules);
    return (migraphx_status)take_status_for("migraphx_modules_destroy");
}

EXPORT migraphx_status migraphx_modules_create(migraphx_modules_t* out, const migraphx_module_t* values, size_t count)
{
    if(out == NULL || count > 16 || (count != 0 && values == NULL)) return migraphx_status_bad_param;
    if(take_null_for("migraphx_modules_create")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_modules_create"); }
    *out = (migraphx_modules_t)create_m12(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->count = count;
    if(count != 0) memcpy((*out)->values, values, count * sizeof(values[0]));
    return (migraphx_status)take_status_for("migraphx_modules_create");
}

EXPORT migraphx_status migraphx_modules_assign_to(migraphx_modules_t output, const migraphx_modules_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_modules_assign_to");
}

EXPORT migraphx_status migraphx_module_create(migraphx_module_t* out, const char* name)
{
    if(out == NULL || name == NULL) return migraphx_status_bad_param;
    *out = (migraphx_module_t)create_m12(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->value = ATOMIC_INCREMENT(next_value);
    copy_string((*out)->name, sizeof((*out)->name), name);
    return (migraphx_status)take_status_for("migraphx_module_create");
}

EXPORT migraphx_status migraphx_module_print(migraphx_module_t module)
{
    if(module == NULL) return migraphx_status_bad_param;
    return (migraphx_status)take_status_for("migraphx_module_print");
}

static migraphx_status add_fake_instruction(migraphx_instruction_t* out, const char* entry)
{
    if(out == NULL) return migraphx_status_bad_param;
    if(take_null_for(entry)) { *out = NULL; return (migraphx_status)take_status_for(entry); }
    *out = (migraphx_instruction_t)create_m12(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->value = ATOMIC_INCREMENT(next_value);
    return (migraphx_status)take_status_for(entry);
}

static migraphx_status dispatch_provider_callback(migraphx_operation_t operation)
{
    char callback_message[16] = {0};
    migraphx_status status;
    if(provider_callback_dispatch == 0 || registered_custom_op == NULL ||
       !registered_custom_op->registered || registered_custom_op->compute_shape == NULL ||
       strcmp(operation->name, registered_custom_op->name) != 0)
        return migraphx_status_success;
    ATOMIC_INCREMENT(provider_callback_dispatch_count);
    status = ((fake_custom_compute_shape)registered_custom_op->compute_shape)(
        NULL, registered_custom_op->object, callback_message, sizeof(callback_message), NULL);
    copy_string(provider_callback_message, sizeof(provider_callback_message), callback_message);
    return status;
}

EXPORT migraphx_status migraphx_module_add_instruction(migraphx_instruction_t* out, migraphx_module_t module, migraphx_operation_t operation, migraphx_instructions_t arguments)
{
    migraphx_status status;
    if(module == NULL || operation == NULL || arguments == NULL) return migraphx_status_bad_param;
    status = add_fake_instruction(out, "migraphx_module_add_instruction");
    if(status != migraphx_status_success || out == NULL || *out == NULL) return status;
    status = dispatch_provider_callback(operation);
    if(status != migraphx_status_success)
    {
        migraphx_instruction_destroy(*out);
        *out = NULL;
    }
    return status;
}

EXPORT migraphx_status migraphx_module_add_instruction_with_mod_args(migraphx_instruction_t* out, migraphx_module_t module, migraphx_operation_t operation, migraphx_instructions_t arguments, migraphx_modules_t modules)
{
    if(module == NULL || operation == NULL || arguments == NULL || modules == NULL) return migraphx_status_bad_param;
    return add_fake_instruction(out, "migraphx_module_add_instruction_with_mod_args");
}

EXPORT migraphx_status migraphx_module_add_literal(migraphx_instruction_t* out, migraphx_module_t module, const migraphx_shape_t shape, const void* buffer)
{
    if(module == NULL || shape == NULL || (shape->bytes != 0 && buffer == NULL)) return migraphx_status_bad_param;
    return add_fake_instruction(out, "migraphx_module_add_literal");
}

EXPORT migraphx_status migraphx_module_add_parameter(migraphx_instruction_t* out, migraphx_module_t module, const char* name, const migraphx_shape_t shape)
{
    if(module == NULL || name == NULL || shape == NULL) return migraphx_status_bad_param;
    return add_fake_instruction(out, "migraphx_module_add_parameter");
}

EXPORT migraphx_status migraphx_module_add_return(migraphx_instruction_t* out, migraphx_module_t module, migraphx_instructions_t arguments)
{
    if(module == NULL || arguments == NULL) return migraphx_status_bad_param;
    return add_fake_instruction(out, "migraphx_module_add_return");
}

EXPORT migraphx_status migraphx_module_add_allocation(migraphx_instruction_t* out, migraphx_module_t module, const migraphx_shape_t shape)
{
    if(module == NULL || shape == NULL) return migraphx_status_bad_param;
    return add_fake_instruction(out, "migraphx_module_add_allocation");
}

EXPORT migraphx_status migraphx_program_get_main_module(migraphx_module_t* out, migraphx_program_t program)
{
    if(out == NULL || program == NULL) return migraphx_status_bad_param;
    *out = take_null_for("migraphx_program_get_main_module") ? NULL : &program->main_module;
    return (migraphx_status)take_status_for("migraphx_program_get_main_module");
}

EXPORT migraphx_status migraphx_program_create_module(migraphx_module_t* out, migraphx_program_t program, const char* name)
{
    migraphx_module_t module;
    if(out == NULL || program == NULL || name == NULL || program->module_count >= 8) return migraphx_status_bad_param;
    module = &program->modules[program->module_count++];
    module->value = ATOMIC_INCREMENT(next_value);
    copy_string(module->name, sizeof(module->name), name);
    *out = take_null_for("migraphx_program_create_module") ? NULL : module;
    return (migraphx_status)take_status_for("migraphx_program_create_module");
}

EXPORT migraphx_status migraphx_program_print(migraphx_program_t program)
{
    if(program == NULL) return migraphx_status_bad_param;
    ATOMIC_INCREMENT(program_print_count);
    return (migraphx_status)take_status_for("migraphx_program_print");
}

EXPORT migraphx_status migraphx_program_sort(migraphx_program_t program)
{
    if(program == NULL) return migraphx_status_bad_param;
    ATOMIC_INCREMENT(program_sort_count);
    program->value += 1;
    return (migraphx_status)take_status_for("migraphx_program_sort");
}

EXPORT migraphx_status migraphx_program_experimental_get_context(migraphx_context_t* out, migraphx_program_t program)
{
    if(out == NULL || program == NULL) return migraphx_status_bad_param;
    *out = take_null_for("migraphx_program_experimental_get_context") ? NULL : (migraphx_context_t)program;
    return (migraphx_status)take_status_for("migraphx_program_experimental_get_context");
}

EXPORT migraphx_status migraphx_operation_destroy(migraphx_operation_t operation)
{
    destroy_m12(operation);
    return (migraphx_status)take_status_for("migraphx_operation_destroy");
}

EXPORT migraphx_status migraphx_operation_create(migraphx_operation_t* out, const char* name, const char* attributes, ...)
{
    migraphx_operation_t operation;
    (void)attributes;
    if(out == NULL || name == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_operation_create"))
    {
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_operation_create");
    }
    operation = (migraphx_operation_t)create_m12(sizeof(*operation));
    if(operation == NULL) return migraphx_status_unknown_error;
    copy_string(operation->name, sizeof(operation->name), name);
    *out = operation;
    return (migraphx_status)take_status_for("migraphx_operation_create");
}

EXPORT migraphx_status migraphx_operation_assign_to(migraphx_operation_t output, const migraphx_operation_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_operation_assign_to");
}

EXPORT migraphx_status migraphx_operation_name(char* out, size_t out_size, const migraphx_operation_t operation)
{
    if(out == NULL || out_size == 0 || operation == NULL) return migraphx_status_bad_param;
    copy_string(out, out_size, operation->name);
    return (migraphx_status)take_status_for("migraphx_operation_name");
}

EXPORT migraphx_status migraphx_onnx_options_assign_to(migraphx_onnx_options_t output, const migraphx_onnx_options_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_onnx_options_assign_to");
}

EXPORT migraphx_status migraphx_file_options_assign_to(migraphx_file_options_t output, const migraphx_file_options_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_file_options_assign_to");
}

EXPORT migraphx_status migraphx_compile_options_assign_to(migraphx_compile_options_t output, const migraphx_compile_options_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_compile_options_assign_to");
}

EXPORT migraphx_status migraphx_tf_options_destroy(migraphx_tf_options_t options)
{
    destroy_m12(options);
    return (migraphx_status)take_status_for("migraphx_tf_options_destroy");
}

EXPORT migraphx_status migraphx_tf_options_create(migraphx_tf_options_t* out)
{
    if(out == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_tf_options_create")) { *out = NULL; return (migraphx_status)take_status_for("migraphx_tf_options_create"); }
    *out = (migraphx_tf_options_t)create_m12(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    return (migraphx_status)take_status_for("migraphx_tf_options_create");
}

EXPORT migraphx_status migraphx_tf_options_assign_to(migraphx_tf_options_t output, const migraphx_tf_options_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_tf_options_assign_to");
}

EXPORT migraphx_status migraphx_tf_options_set_nhwc(migraphx_tf_options_t options, uint8_t enabled)
{
    if(options == NULL) return migraphx_status_bad_param;
    options->nhwc = enabled;
    return (migraphx_status)take_status_for("migraphx_tf_options_set_nhwc");
}

EXPORT migraphx_status migraphx_tf_options_set_input_parameter_shape(migraphx_tf_options_t options, const char* name, const size_t* values, size_t count)
{
    if(options == NULL || name == NULL || count > 8 || (count != 0 && values == NULL)) return migraphx_status_bad_param;
    options->static_count = count;
    if(count != 0) memcpy(options->static_values, values, count * sizeof(values[0]));
    return (migraphx_status)take_status_for("migraphx_tf_options_set_input_parameter_shape");
}

EXPORT migraphx_status migraphx_tf_options_set_default_dim_value(migraphx_tf_options_t options, size_t value)
{
    if(options == NULL) return migraphx_status_bad_param;
    options->default_dim = value;
    return (migraphx_status)take_status_for("migraphx_tf_options_set_default_dim_value");
}

EXPORT migraphx_status migraphx_tf_options_set_output_names(migraphx_tf_options_t options, const char* const* names, size_t count)
{
    size_t index;
    if(options == NULL || count > 8 || (count != 0 && names == NULL)) return migraphx_status_bad_param;
    options->output_count = count;
    for(index = 0; index < count; ++index)
    {
        if(names[index] == NULL) return migraphx_status_bad_param;
        copy_string(options->output_names[index], sizeof(options->output_names[index]), names[index]);
    }
    return (migraphx_status)take_status_for("migraphx_tf_options_set_output_names");
}

static migraphx_status parse_tf_core(migraphx_program_t* out, const void* data, size_t size, migraphx_tf_options_t options, const char* entry)
{
    migraphx_status status;
    if(out == NULL || data == NULL || size == 0 || options == NULL) return migraphx_status_bad_param;
    *out = NULL;
    status = migraphx_program_create(out);
    if(status != migraphx_status_success)
    {
        if(out != NULL && *out != NULL)
        {
            migraphx_program_destroy(*out);
            *out = NULL;
        }
        return status;
    }
    if(take_null_for(entry)) { migraphx_program_destroy(*out); *out = NULL; return (migraphx_status)take_status_for(entry); }
    (*out)->parsed = 2;
    (*out)->static_count = options->static_count;
    memcpy((*out)->static_values, options->static_values, sizeof(options->static_values));
    return (migraphx_status)take_status_for(entry);
}

EXPORT migraphx_status migraphx_parse_tf(migraphx_program_t* out, const char* name, migraphx_tf_options_t options)
{
    if(name == NULL) return migraphx_status_bad_param;
    copy_string(last_model_path, sizeof(last_model_path), name);
    return parse_tf_core(out, name, strlen(name), options, "migraphx_parse_tf");
}

EXPORT migraphx_status migraphx_parse_tf_buffer(migraphx_program_t* out, const void* data, size_t size, migraphx_tf_options_t options)
{
    ATOMIC_EXCHANGE(last_model_size, (int)size);
    return parse_tf_core(out, data, size, options, "migraphx_parse_tf_buffer");
}

EXPORT migraphx_status migraphx_quantize_op_names_destroy(migraphx_quantize_op_names_t names)
{
    destroy_m12(names);
    return (migraphx_status)take_status_for("migraphx_quantize_op_names_destroy");
}

EXPORT migraphx_status migraphx_quantize_op_names_create(migraphx_quantize_op_names_t* out)
{
    if(out == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_quantize_op_names_create"))
    {
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_quantize_op_names_create");
    }
    *out = (migraphx_quantize_op_names_t)create_m12(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    return (migraphx_status)take_status_for("migraphx_quantize_op_names_create");
}

EXPORT migraphx_status migraphx_quantize_op_names_assign_to(migraphx_quantize_op_names_t output, const migraphx_quantize_op_names_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_quantize_op_names_assign_to");
}

EXPORT migraphx_status migraphx_quantize_op_names_add(migraphx_quantize_op_names_t names, const char* name)
{
    if(names == NULL || name == NULL || names->count >= 16) return migraphx_status_bad_param;
    copy_string(names->names[names->count++], sizeof(names->names[0]), name);
    return (migraphx_status)take_status_for("migraphx_quantize_op_names_add");
}

static migraphx_status set_quantization(migraphx_program_t program, int value, const char* entry)
{
    if(program == NULL) return migraphx_status_bad_param;
    program->quantization = value;
    ATOMIC_EXCHANGE(last_quantization, value);
    return (migraphx_status)take_status_for(entry);
}

EXPORT migraphx_status migraphx_quantize_fp16_with_op_names(migraphx_program_t program, migraphx_quantize_op_names_t names)
{
    if(names == NULL) return migraphx_status_bad_param;
    return set_quantization(program, 16, "migraphx_quantize_fp16_with_op_names");
}
EXPORT migraphx_status migraphx_quantize_fp16(migraphx_program_t program) { return set_quantization(program, 16, "migraphx_quantize_fp16"); }
EXPORT migraphx_status migraphx_quantize_bf16_with_op_names(migraphx_program_t program, migraphx_quantize_op_names_t names)
{
    if(names == NULL) return migraphx_status_bad_param;
    return set_quantization(program, 116, "migraphx_quantize_bf16_with_op_names");
}
EXPORT migraphx_status migraphx_quantize_bf16(migraphx_program_t program) { return set_quantization(program, 116, "migraphx_quantize_bf16"); }

EXPORT migraphx_status migraphx_quantize_int8_options_destroy(migraphx_quantize_int8_options_t options)
{
    destroy_m12(options);
    return (migraphx_status)take_status_for("migraphx_quantize_int8_options_destroy");
}
EXPORT migraphx_status migraphx_quantize_int8_options_create(migraphx_quantize_int8_options_t* out)
{
    if(out == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_quantize_int8_options_create"))
    {
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_quantize_int8_options_create");
    }
    *out = (migraphx_quantize_int8_options_t)create_m12(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    return (migraphx_status)take_status_for("migraphx_quantize_int8_options_create");
}
EXPORT migraphx_status migraphx_quantize_int8_options_assign_to(migraphx_quantize_int8_options_t output, const migraphx_quantize_int8_options_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_quantize_int8_options_assign_to");
}
EXPORT migraphx_status migraphx_quantize_int8_options_add_op_name(migraphx_quantize_int8_options_t options, const char* name)
{
    if(options == NULL || name == NULL || options->count >= 16) return migraphx_status_bad_param;
    copy_string(options->names[options->count++], sizeof(options->names[0]), name);
    return (migraphx_status)take_status_for("migraphx_quantize_int8_options_add_op_name");
}
EXPORT migraphx_status migraphx_quantize_int8_options_add_calibration_data(migraphx_quantize_int8_options_t options, migraphx_program_parameters_t data)
{
    if(options == NULL || data == NULL) return migraphx_status_bad_param;
    options->calibration_count += 1;
    return (migraphx_status)take_status_for("migraphx_quantize_int8_options_add_calibration_data");
}
EXPORT migraphx_status migraphx_quantize_int8(migraphx_program_t program, migraphx_target_t target, migraphx_quantize_int8_options_t options)
{
    if(target == NULL || options == NULL) return migraphx_status_bad_param;
    return set_quantization(program, 8, "migraphx_quantize_int8");
}

EXPORT migraphx_status migraphx_quantize_fp8_options_destroy(migraphx_quantize_fp8_options_t options)
{
    destroy_m12(options);
    return (migraphx_status)take_status_for("migraphx_quantize_fp8_options_destroy");
}
EXPORT migraphx_status migraphx_quantize_fp8_options_create(migraphx_quantize_fp8_options_t* out)
{
    if(out == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_quantize_fp8_options_create"))
    {
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_quantize_fp8_options_create");
    }
    *out = (migraphx_quantize_fp8_options_t)create_m12(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    return (migraphx_status)take_status_for("migraphx_quantize_fp8_options_create");
}
EXPORT migraphx_status migraphx_quantize_fp8_options_assign_to(migraphx_quantize_fp8_options_t output, const migraphx_quantize_fp8_options_t input)
{
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    *output = *input;
    return (migraphx_status)take_status_for("migraphx_quantize_fp8_options_assign_to");
}
EXPORT migraphx_status migraphx_quantize_fp8_options_add_calibration_data(migraphx_quantize_fp8_options_t options, migraphx_program_parameters_t data)
{
    if(options == NULL || data == NULL) return migraphx_status_bad_param;
    options->calibration_count += 1;
    return (migraphx_status)take_status_for("migraphx_quantize_fp8_options_add_calibration_data");
}
EXPORT migraphx_status migraphx_quantize_fp8(migraphx_program_t program, migraphx_target_t target, migraphx_quantize_fp8_options_t options)
{
    if(target == NULL || options == NULL) return migraphx_status_bad_param;
    return set_quantization(program, 18, "migraphx_quantize_fp8");
}

EXPORT migraphx_status migraphx_context_finish(migraphx_context_t context)
{
    if(context == NULL) return migraphx_status_bad_param;
    ATOMIC_INCREMENT(context_finish_count);
    return (migraphx_status)take_status_for("migraphx_context_finish");
}
EXPORT migraphx_status migraphx_context_get_queue(void** out, migraphx_context_t context)
{
    if(out == NULL || context == NULL) return migraphx_status_bad_param;
    *out = context;
    return (migraphx_status)take_status_for("migraphx_context_get_queue");
}

EXPORT migraphx_status migraphx_experimental_custom_op_create(
    migraphx_experimental_custom_op_t* out, void* object, fake_custom_copy copy, fake_custom_delete delete_object, const char* object_type_name, const char* name)
{
    if(out == NULL || object == NULL || copy == NULL || delete_object == NULL || object_type_name == NULL || name == NULL) return migraphx_status_bad_param;
    if(take_null_for("migraphx_experimental_custom_op_create"))
    {
        *out = NULL;
        return (migraphx_status)take_status_for("migraphx_experimental_custom_op_create");
    }
    *out = (migraphx_experimental_custom_op_t)create_m12(sizeof(**out));
    if(*out == NULL) return migraphx_status_unknown_error;
    (*out)->object = object; (*out)->copy = copy; (*out)->delete_object = delete_object;
    copy_string((*out)->object_type_name, sizeof((*out)->object_type_name), object_type_name);
    copy_string((*out)->name, sizeof((*out)->name), name);
    return (migraphx_status)take_status_for("migraphx_experimental_custom_op_create");
}

EXPORT migraphx_status migraphx_experimental_custom_op_destroy(migraphx_experimental_custom_op_t operation)
{
    migraphx_status status = migraphx_status_success;
    if(registered_custom_op == operation) registered_custom_op = NULL;
    if(operation != NULL && operation->delete_object != NULL) status = operation->delete_object(operation->object);
    destroy_m12(operation);
    if(status != migraphx_status_success) return status;
    return (migraphx_status)take_status_for("migraphx_experimental_custom_op_destroy");
}

EXPORT migraphx_status migraphx_experimental_custom_op_assign_to(migraphx_experimental_custom_op_t output, const migraphx_experimental_custom_op_t input)
{
    void* copied = NULL;
    if(output == NULL || input == NULL) return migraphx_status_bad_param;
    if(input->copy(&copied, input->object) != migraphx_status_success) return migraphx_status_unknown_error;
    if(output->delete_object != NULL) output->delete_object(output->object);
    *output = *input;
    output->object = copied;
    return (migraphx_status)take_status_for("migraphx_experimental_custom_op_assign_to");
}

EXPORT migraphx_status migraphx_experimental_custom_op_set_compute(migraphx_experimental_custom_op_t operation, void* callback)
{
    if(operation == NULL) return migraphx_status_bad_param;
    operation->compute = callback;
    return (migraphx_status)take_status_for("migraphx_experimental_custom_op_set_compute");
}
EXPORT migraphx_status migraphx_experimental_custom_op_set_compute_shape(migraphx_experimental_custom_op_t operation, void* callback)
{
    if(operation == NULL) return migraphx_status_bad_param;
    operation->compute_shape = callback;
    return (migraphx_status)take_status_for("migraphx_experimental_custom_op_set_compute_shape");
}
EXPORT migraphx_status migraphx_experimental_custom_op_set_output_alias(migraphx_experimental_custom_op_t operation, void* callback)
{
    if(operation == NULL) return migraphx_status_bad_param;
    operation->output_alias = callback;
    return (migraphx_status)take_status_for("migraphx_experimental_custom_op_set_output_alias");
}
EXPORT migraphx_status migraphx_experimental_custom_op_set_runs_on_offload_target(migraphx_experimental_custom_op_t operation, void* callback)
{
    if(operation == NULL) return migraphx_status_bad_param;
    operation->runs_on_offload_target = callback;
    return (migraphx_status)take_status_for("migraphx_experimental_custom_op_set_runs_on_offload_target");
}
EXPORT migraphx_status migraphx_experimental_custom_op_register(migraphx_experimental_custom_op_t operation)
{
    if(operation == NULL) return migraphx_status_bad_param;
    operation->registered = 1;
    registered_custom_op = operation;
    ATOMIC_INCREMENT(custom_register_count);
    return (migraphx_status)take_status_for("migraphx_experimental_custom_op_register");
}

/* Test-only entry point: exercise the managed callback ABI with opaque inputs. */
EXPORT migraphx_status fake_invoke_custom_callbacks(migraphx_experimental_custom_op_t operation)
{
    migraphx_status status;
    if(operation == NULL) return migraphx_status_bad_param;
    if(operation->compute != NULL)
    {
        status = ((fake_custom_compute)operation->compute)(NULL, operation->object, NULL, 0, NULL, NULL, NULL);
        if(status != migraphx_status_success) return status;
    }
    if(operation->compute_shape != NULL)
    {
        status = ((fake_custom_compute_shape)operation->compute_shape)(NULL, operation->object, NULL, 0, NULL);
        if(status != migraphx_status_success) return status;
    }
    if(operation->output_alias != NULL)
    {
        status = ((fake_custom_output_alias)operation->output_alias)(NULL, NULL, operation->object, NULL, 0, NULL);
        if(status != migraphx_status_success) return status;
    }
    if(operation->runs_on_offload_target != NULL)
    {
        status = ((fake_custom_runs_on_offload_target)operation->runs_on_offload_target)(NULL, operation->object, NULL, 0);
        if(status != migraphx_status_success) return status;
    }
    return migraphx_status_success;
}

/* Test-only entry point: provide an exception buffer to the compute callback. */
EXPORT migraphx_status fake_invoke_custom_compute_with_error_buffer(
    migraphx_experimental_custom_op_t operation, void* exception_message, size_t exception_message_size)
{
    if(operation == NULL || operation->compute == NULL) return migraphx_status_bad_param;
    return ((fake_custom_compute)operation->compute)(NULL, operation->object, exception_message, exception_message_size, NULL, NULL, NULL);
}
