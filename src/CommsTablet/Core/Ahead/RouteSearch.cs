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
        private volatile bool done;

        public RouteSearch(TrackGraph graph, RouteRequest request)
        {
            var thread = new Thread(() => Run(graph, request)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "CommsTablet route search" };
            thread.Start();
        }

        public bool Done => done;

        /// <summary>The route, once done; null if there is none.</summary>
        public Route Route { get; private set; }

        public RouteFailure Failure { get; private set; }

        /// <summary>What went wrong, if the search itself failed.</summary>
        public Exception Error { get; private set; }

        /// <summary>Whether the route runs through standing cars, because no other way was found.</summary>
        public bool ThroughCars { get; private set; }

        private void Run(TrackGraph graph, RouteRequest request)
        {
            try
            {
                var router = new Router(graph);
                var route = router.Find(request, out var failure);
                if (route == null && failure == RouteFailure.NoPath && request.Blocked != null)
                {
                    request.Blocked = null;
                    route = router.Find(request, out failure);
                    ThroughCars = route != null;
                }
                Route = route;
                Failure = failure;
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
