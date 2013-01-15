namespace fakeSMTP.Commands
{
    public interface ICommand
    {
        Context Context { get; set; }

        string GetResponse();
    }
}
