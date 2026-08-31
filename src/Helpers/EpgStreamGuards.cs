namespace Sportarr.Api.Helpers;

/// <summary>
/// Read-only stream wrappers for the EPG download path.
///
/// The guide used to be read whole: the compressed bytes, a second copy from
/// decompression, a UTF-16 string of the text and a full XML document were
/// all alive at once, and only the compressed bytes had a ceiling. A guide is
/// parsed as it downloads now, and these two wrappers carry the two guards
/// that whole-buffer path had for free: a size ceiling and a read deadline.
/// </summary>
public static class EpgStreamGuards
{
    /// <summary>
    /// Puts back bytes that were read to identify the stream. Gzip is
    /// detected by its first two bytes, and the decompressor needs to see
    /// those same bytes again.
    /// </summary>
    public sealed class PushbackStream : Stream
    {
        private readonly Stream _inner;
        private readonly byte[] _head;
        private int _headPos;

        public PushbackStream(byte[] head, int headLength, Stream inner)
        {
            _inner = inner;
            _head = head[..headLength];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_headPos < _head.Length)
            {
                var n = Math.Min(count, _head.Length - _headPos);
                Array.Copy(_head, _headPos, buffer, offset, n);
                _headPos += n;
                return n;
            }

            return _inner.Read(buffer, offset, count);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Enforces a total-bytes ceiling on reads. Placed after the
    /// decompressor, so the ceiling is on what the guide expands to, which
    /// is the number that matters. The old path bounded only the compressed
    /// download, and a small archive that opened into gigabytes went
    /// straight past it.
    ///
    /// No deadline lives here. A deadline checked between reads cannot
    /// interrupt a read that never returns, so time is enforced where the
    /// network is: the download is spooled to disk first under a
    /// cancellation deadline, and this stream only ever reads that file.
    /// </summary>
    public sealed class GuardedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private readonly string _what;
        private long _read;

        public GuardedReadStream(Stream inner, long maxBytes, string what)
        {
            _inner = inner;
            _maxBytes = maxBytes;
            _what = what;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _inner.Read(buffer, offset, count);
            _read += n;

            if (_read > _maxBytes)
            {
                throw new InvalidDataException(
                    $"{_what} is larger than the {_maxBytes / (1024 * 1024)} MB limit once decompressed.");
            }

            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
