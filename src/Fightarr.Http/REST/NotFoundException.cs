using System.Net;
using Fightarr.Http.Exceptions;

namespace Fightarr.Http.REST
{
    public class NotFoundException : ApiException
    {
        public NotFoundException(object content = null)
            : base(HttpStatusCode.NotFound, content)
        {
        }
    }
}
