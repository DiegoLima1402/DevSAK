using System.Collections.Generic;

namespace DevSAK.Services;

// Tracks operations even when their page has been left and a new page is opened.
public static class OperationActivity
{
    private static readonly HashSet<object> Active = new();
    public static bool IsBusy { get { lock (Active) return Active.Count > 0; } }

    public static void SetBusy(object owner, bool busy)
    {
        lock (Active)
        {
            if (busy) Active.Add(owner);
            else Active.Remove(owner);
        }
    }
}
