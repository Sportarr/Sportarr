import { useEvents } from '../api/hooks';

export default function EventsPage() {
  const { data: events, isLoading, error } = useEvents();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-500"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-red-900 border border-red-700 text-red-100 px-4 py-3 rounded">
        <p className="font-bold">Error loading events</p>
        <p className="text-sm">{(error as Error).message}</p>
      </div>
    );
  }

  if (!events || events.length === 0) {
    return (
      <div className="text-center py-12">
        <h2 className="text-2xl font-bold mb-4">No Events</h2>
        <p className="text-gray-400">
          You haven't added any events yet. Click "Add Event" to get started.
        </p>
        <button className="mt-4 bg-blue-600 hover:bg-blue-700 text-white font-bold py-2 px-4 rounded">
          Add Event
        </button>
      </div>
    );
  }

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-3xl font-bold">Events</h1>
        <button className="bg-blue-600 hover:bg-blue-700 text-white font-bold py-2 px-4 rounded">
          Add Event
        </button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {events.map((event) => (
          <div
            key={event.id}
            className="bg-gray-800 rounded-lg overflow-hidden shadow-lg hover:shadow-xl transition-shadow"
          >
            {event.images?.[0] && (
              <img
                src={event.images[0].remoteUrl}
                alt={event.title}
                className="w-full h-48 object-cover"
              />
            )}
            <div className="p-4">
              <h3 className="text-lg font-bold mb-2">{event.title}</h3>
              <p className="text-gray-400 text-sm mb-2">{event.organization}</p>
              <p className="text-gray-400 text-sm mb-2">
                {new Date(event.eventDate).toLocaleDateString()}
              </p>
              {event.venue && (
                <p className="text-gray-400 text-sm">{event.venue}</p>
              )}
              <div className="mt-4 flex items-center justify-between">
                <span
                  className={`px-2 py-1 text-xs rounded ${
                    event.monitored
                      ? 'bg-green-900 text-green-100'
                      : 'bg-gray-700 text-gray-300'
                  }`}
                >
                  {event.monitored ? 'Monitored' : 'Unmonitored'}
                </span>
                {event.hasFile && (
                  <span className="text-green-400 text-sm">✓ Downloaded</span>
                )}
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
