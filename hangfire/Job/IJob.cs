namespace hangfire.Job {
    public interface IJob<TArgs> {
        Task Run(TArgs args);
    }
}