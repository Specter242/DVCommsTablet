using System;
using System.Threading;
using CommsTablet.Core.Routing;
using CommsTablet.Core.Tracks;

namespace CommsTablet.Core.Ahead
{
    /// <summary>
    /// One route search on a background thread, so that a long one costs the
    /// game no frame. The request must hold only what is safe to read there:
    /// the graph and copies of the game's state. If standing cars leave no way
    /// through, it looks again without them, since the player can move cars and
    /// a route that needs that still says more than none.
    /// </summary>
    public sealed class RouteSearch
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private volatile bool done;
        private volatile bool cancelled;

        public RouteSearch(TrackGraph graph, RouteRequest request)
        {
            var thread = new Thread(() => Run(graph, request)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "CommsTablet route search" };
            thread.Start();
        }

        public bool Done => done;

        /// <summary>Whether this search was asked to stop early.</summary>
        public bool Cancelled => cancelled;

        /// <summary>The route, once done; null if there is none.</summary>
        public Route Route { get; private set; }

        public RouteFailure Failure { get; private set; }

        /// <summary>What went wrong, if the search itself failed.</summary>
        public Exception Error { get; private set; }

        /// <summary>Whether the route runs through standing cars, because no other way was found.</summary>
        public bool ThroughCars { get; private set; }

        /// <summary>Asks the background search to stop early; safe to call from any thread and more than once.</summary>
        public void Cancel()
        {
            cancelled = true;
            cancellation.Cancel();
        }

        private void Run(TrackGraph graph, RouteRequest request)
        {
            var token = cancellation.Token;
            try
            {
                if (token.IsCancellationRequested)
                {
                    Failure = RouteFailure.Cancelled;
                    done = true;
                    return;
                }
                var router = new Router(graph);
                var route = router.Find(request, out var failure, token);
                if (!token.IsCancellationRequested && route == null && failure == RouteFailure.NoPath && request.Blocked != null)
                {
                    request.Blocked = null;
                    route = router.Find(request, out failure, token);
                    ThroughCars = route != null && !token.IsCancellationRequested;
                }
                if (token.IsCancellationRequested)
                {
                    Route = null;
                    ThroughCars = false;
                    Failure = RouteFailure.Cancelled;
                }
                else
                {
                    Route = route;
                    Failure = failure;
                }
            }
            catch (Exception e)
            {
                // An exception left to end this thread would take the game with it.
                Error = e;
                Failure = RouteFailure.NoPath;
            }
            done = true;
        }
    }
}
