namespace AudiobookManager.Api.Async;

public interface IOrganize
{
    Task UpdateProgress(ProgressUpdate progressUpdate);
}
