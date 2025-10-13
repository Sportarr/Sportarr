interface PlaceholderPageProps {
  title: string;
  description?: string;
}

export default function PlaceholderPage({ title, description }: PlaceholderPageProps) {
  return (
    <div className="flex items-center justify-center h-full">
      <div className="text-center max-w-2xl px-8">
        <div className="mb-8">
          <div className="inline-block p-8 bg-red-950/20 rounded-full border-2 border-red-900/30">
            <svg
              className="w-20 h-20 text-red-600/50"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={1.5}
                d="M12 6v6m0 0v6m0-6h6m-6 0H6"
              />
            </svg>
          </div>
        </div>
        <h1 className="text-5xl font-bold text-white mb-4">{title}</h1>
        {description && (
          <p className="text-xl text-gray-400 mb-8">{description}</p>
        )}
        <div className="inline-block px-6 py-3 bg-gray-900 border border-red-900/30 text-gray-400 rounded-lg">
          <p className="text-sm">This feature is coming soon</p>
        </div>
      </div>
    </div>
  );
}
