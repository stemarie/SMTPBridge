namespace SMTPLibrary
{
    public interface IProcessor
    {
        Context Context { get; set; }

        void Process(string message);
    }
}