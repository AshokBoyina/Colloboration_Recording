namespace NICE.Platform.Collaboration.Core.Exceptions;
public class AgentCapacityExceededException : Exception
{
    public AgentCapacityExceededException() : base("Agent has reached maximum collaboration limit.") { }
    public AgentCapacityExceededException(string message) : base(message) { }
}
