using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WorldPinball.Api.Filters.ModelBindingValidation;

public sealed class ValidationFilter :IAsyncActionFilter
{

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context, 
        ActionExecutionDelegate next)
    {
        if (!context.ModelState.IsValid)
        {
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState));
            return; 
        }
        
        var argument = context.ActionArguments.Values.FirstOrDefault(v => v is not null);
        if (argument is null)
        {
            await next();
            return;
        }

        var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

        if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
        {
            await next();
            return;
        }
        
        // 1. Prepare validation context and execute validation asynchronously
        var validationContext = new ValidationContext<object>(argument);
        var validationResult = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);
        // 2. If invalid, populate ModelState and short-circuit the pipeline
        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                context.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState));
            return;
        }
         // 3. Validation passed, proceed to the action
        await next();
    }

    public void OnActionExecutedAsync(ActionExecutedContext context)
    {
       
    }

}