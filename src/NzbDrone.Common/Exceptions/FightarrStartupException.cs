using System;

namespace NzbDrone.Common.Exceptions
{
    public class FightarrStartupException : NzbDroneException
    {
        public FightarrStartupException(string message, params object[] args)
            : base("Fightarr failed to start: " + string.Format(message, args))
        {
        }

        public FightarrStartupException(string message)
            : base("Fightarr failed to start: " + message)
        {
        }

        public FightarrStartupException()
            : base("Fightarr failed to start")
        {
        }

        public FightarrStartupException(Exception innerException, string message, params object[] args)
            : base("Fightarr failed to start: " + string.Format(message, args), innerException)
        {
        }

        public FightarrStartupException(Exception innerException, string message)
            : base("Fightarr failed to start: " + message, innerException)
        {
        }

        public FightarrStartupException(Exception innerException)
            : base("Fightarr failed to start: " + innerException.Message)
        {
        }
    }
}
