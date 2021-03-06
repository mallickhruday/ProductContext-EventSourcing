using EventStore.ClientAPI.SystemData;

namespace AggregateSource.EventStore.Tests.Stubs
{
    public class StubbedStreamUserCredentialsResolver : IStreamUserCredentialsResolver
    {
        public static readonly IStreamUserCredentialsResolver Instance = new StubbedStreamUserCredentialsResolver();

        private StubbedStreamUserCredentialsResolver()
        {
        }

        public UserCredentials Resolve(string identifier) => new UserCredentials("", "");
    }
}
