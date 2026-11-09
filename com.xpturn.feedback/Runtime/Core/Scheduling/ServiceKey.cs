namespace xpTURN.Feedback
{
    // an extension service's key. A service type keeps one in a static readonly field; its number comes from a domain-wide
    // counter when the key is made — dense from 0 and never reset (a key outlives a session with the domain reload off, and a
    // reset would give its number to a new key). Main thread only
    internal sealed class ServiceKey
    {
        static int s_Count;

        // the index of the factory table and of each context's instances
        internal readonly int Id;

        internal ServiceKey()
        {
            Id = s_Count++;
        }
    }
}
