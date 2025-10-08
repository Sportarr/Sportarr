using System;
using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.MetadataSource.Fightarr
{
    public class FightarrException : NzbDroneException
    {
        public FightarrException(string message, params object[] args)
            : base(string.Format(message, args))
        {
        }

        public FightarrException(string message)
            : base(message)
        {
        }

        public FightarrException(string message, Exception innerException, params object[] args)
            : base(string.Format(message, args), innerException)
        {
        }
    }
}
