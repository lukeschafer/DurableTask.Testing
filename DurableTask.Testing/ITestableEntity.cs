namespace DurableTask.Testing
{
    public interface ITestableEntity<T>
    {
        T GetState();
    }
}
