import { useState } from 'react';
import { CalendarIcon, MapPinIcon, GlobeAltIcon, UsersIcon } from '@heroicons/react/24/outline';

interface SearchResultProps {
  event: {
    tapologyId: string;
    title: string;
    organization: string;
    eventDate: string;
    venue?: string;
    location?: string;
    posterUrl?: string;
    fights?: {
      fighter1: string;
      fighter2: string;
      weightClass?: string;
      isMainEvent: boolean;
    }[];
  };
  onSelect: () => void;
}

export default function AddEventSearchResult({ event, onSelect }: SearchResultProps) {
  const [imageError, setImageError] = useState(false);

  const formatDate = (dateString: string) => {
    try {
      const date = new Date(dateString);
      return date.toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
      });
    } catch {
      return dateString;
    }
  };

  const isUpcoming = new Date(event.eventDate) > new Date();
  const mainEventFight = event.fights?.find(f => f.isMainEvent);

  return (
    <div
      onClick={onSelect}
      className="group bg-gradient-to-br from-gray-900 to-black rounded-lg overflow-hidden border border-red-900/30 hover:border-red-600/50 transition-all duration-300 cursor-pointer hover:shadow-lg hover:shadow-red-600/20"
    >
      <div className="flex">
        {/* Poster Section */}
        <div className="relative w-32 flex-shrink-0 bg-gray-950">
          {event.posterUrl && !imageError ? (
            <img
              src={event.posterUrl}
              alt={event.title}
              className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
              onError={() => setImageError(true)}
            />
          ) : (
            <div className="w-full h-full flex items-center justify-center">
              <svg className="w-16 h-16 text-gray-700" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={1}
                  d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z"
                />
              </svg>
            </div>
          )}

          {/* Status Badge */}
          {isUpcoming && (
            <div className="absolute top-2 left-2">
              <span className="px-2 py-1 bg-green-600 text-white text-xs font-semibold rounded">
                UPCOMING
              </span>
            </div>
          )}
        </div>

        {/* Content Section */}
        <div className="flex-1 p-4">
          {/* Title and Organization */}
          <div className="mb-3">
            <h3 className="text-lg font-bold text-white mb-1 line-clamp-2 group-hover:text-red-400 transition-colors">
              {event.title}
            </h3>
            <p className="text-red-400 font-semibold text-sm flex items-center">
              <GlobeAltIcon className="w-4 h-4 mr-1" />
              {event.organization}
            </p>
          </div>

          {/* Metadata Grid */}
          <div className="grid grid-cols-2 gap-2 mb-3 text-sm">
            {/* Date */}
            <div className="flex items-center text-gray-300">
              <CalendarIcon className="w-4 h-4 mr-2 text-gray-500" />
              <span>{formatDate(event.eventDate)}</span>
            </div>

            {/* Fight Count */}
            {event.fights && event.fights.length > 0 && (
              <div className="flex items-center text-gray-300">
                <UsersIcon className="w-4 h-4 mr-2 text-gray-500" />
                <span>{event.fights.length} {event.fights.length === 1 ? 'fight' : 'fights'}</span>
              </div>
            )}

            {/* Venue/Location */}
            {(event.venue || event.location) && (
              <div className="flex items-center text-gray-300 col-span-2">
                <MapPinIcon className="w-4 h-4 mr-2 text-gray-500 flex-shrink-0" />
                <span className="line-clamp-1">
                  {event.venue && event.location
                    ? `${event.venue}, ${event.location}`
                    : event.venue || event.location}
                </span>
              </div>
            )}
          </div>

          {/* Main Event Info */}
          {mainEventFight && (
            <div className="mt-3 p-2 bg-red-950/30 rounded border border-red-900/50">
              <div className="flex items-center justify-between">
                <span className="text-xs text-red-400 font-semibold">MAIN EVENT</span>
                {mainEventFight.weightClass && (
                  <span className="text-xs text-gray-400">{mainEventFight.weightClass}</span>
                )}
              </div>
              <p className="text-sm text-white mt-1">
                {mainEventFight.fighter1} vs {mainEventFight.fighter2}
              </p>
            </div>
          )}

          {/* Hover Indicator */}
          <div className="mt-3 text-xs text-gray-500 group-hover:text-red-400 transition-colors">
            Click to configure and add →
          </div>
        </div>
      </div>
    </div>
  );
}
