import { useEvents } from '../api/hooks';
import { PlusIcon } from '@heroicons/react/24/outline';

export default function EventsPage() {
  const { data: events, isLoading, error } = useEvents();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="animate-spin rounded-full h-16 w-16 border-b-4 border-red-600"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-8">
        <div className="bg-red-950 border border-red-700 text-red-100 px-6 py-4 rounded-lg">
          <p className="font-bold text-lg">Error Loading Events</p>
          <p className="text-sm mt-2">{(error as Error).message}</p>
        </div>
      </div>
    );
  }

  if (!events || events.length === 0) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-center max-w-md">
          <div className="mb-8">
            <div className="inline-block p-6 bg-red-950/30 rounded-full border-2 border-red-900/50">
              <svg
                className="w-16 h-16 text-red-600"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M12 6v6m0 0v6m0-6h6m-6 0H6"
                />
              </svg>
            </div>
          </div>
          <h2 className="text-3xl font-bold mb-4 text-white">No Events Found</h2>
          <p className="text-gray-400 mb-8">
            Start building your MMA collection by adding your first event.
          </p>
          <button className="inline-flex items-center px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105">
            <PlusIcon className="w-5 h-5 mr-2" />
            Add Event
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="p-8">
      {/* Header */}
      <div className="flex justify-between items-center mb-8">
        <div>
          <h1 className="text-4xl font-bold text-white mb-2">Events</h1>
          <p className="text-gray-400">
            {events.length} {events.length === 1 ? 'event' : 'events'} in your library
          </p>
        </div>
        <button className="inline-flex items-center px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105">
          <PlusIcon className="w-5 h-5 mr-2" />
          Add Event
        </button>
      </div>

      {/* Events Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
        {events.map((event) => (
          <div
            key={event.id}
            className="group bg-gradient-to-br from-gray-900 to-black rounded-lg overflow-hidden border border-red-900/30 hover:border-red-600/50 shadow-xl hover:shadow-2xl hover:shadow-red-900/20 transition-all duration-300 cursor-pointer"
          >
            {/* Event Poster */}
            <div className="relative aspect-[2/3] bg-gray-950">
              {event.images?.[0] ? (
                <img
                  src={event.images[0].remoteUrl}
                  alt={event.title}
                  className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                />
              ) : (
                <div className="w-full h-full flex items-center justify-center bg-gradient-to-br from-gray-900 to-black">
                  <svg
                    className="w-24 h-24 text-gray-700"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      strokeWidth={1}
                      d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z"
                    />
                  </svg>
                </div>
              )}

              {/* Status Overlay */}
              <div className="absolute top-2 right-2 flex gap-2">
                {event.monitored && (
                  <span className="px-2 py-1 bg-red-600/90 backdrop-blur-sm text-white text-xs font-semibold rounded">
                    MONITORED
                  </span>
                )}
                {event.hasFile && (
                  <span className="px-2 py-1 bg-green-600/90 backdrop-blur-sm text-white text-xs font-semibold rounded">
                    ✓
                  </span>
                )}
              </div>
            </div>

            {/* Event Info */}
            <div className="p-4">
              <h3 className="text-lg font-bold text-white mb-2 line-clamp-2 group-hover:text-red-400 transition-colors">
                {event.title}
              </h3>

              <div className="space-y-1 text-sm">
                <p className="text-red-400 font-semibold">{event.organization}</p>

                <p className="text-gray-400">
                  {new Date(event.eventDate).toLocaleDateString('en-US', {
                    year: 'numeric',
                    month: 'long',
                    day: 'numeric',
                  })}
                </p>

                {event.venue && (
                  <p className="text-gray-500 text-xs line-clamp-1">{event.venue}</p>
                )}

                {event.location && (
                  <p className="text-gray-500 text-xs line-clamp-1">{event.location}</p>
                )}
              </div>

              {/* Quality Badge */}
              {event.quality && (
                <div className="mt-3 pt-3 border-t border-gray-800">
                  <span className="inline-block px-2 py-1 bg-gray-800 text-gray-400 text-xs rounded">
                    {event.quality}
                  </span>
                </div>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
