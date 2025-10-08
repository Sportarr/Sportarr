using System;
using Microsoft.AspNetCore.Mvc;

namespace Fightarr.Http.REST.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RestPostByIdAttribute : HttpPostAttribute
    {
    }
}
