using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DaliAPI
{
    public class HttpResponseExceptionFilter : IActionFilter, IOrderedFilter
    {
        public int Order => int.MaxValue - 10;

        public void OnActionExecuting(ActionExecutingContext context) { }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Exception is BadRequestException httpResponseException)
            {
                context.Result = new BadRequestObjectResult(httpResponseException.Value);

                context.ExceptionHandled = true;
            }
        }
    }
}