// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Mvc.Infrastructure
{
    public class HttpRequestStreamReader : TextReader
    {
        private const int DefaultBufferSize = 1024;
        private const int MinBufferSize = 128;
        private const int MaxSharedBuilderCapacity = 360; // also the max capacity used in StringBuilderCache

        private readonly Stream _stream;
        private readonly Encoding _encoding;
        private readonly Decoder _decoder;
        private readonly byte[] _byteBuffer;
        private readonly char[] _charBuffer;

        private int _charBufferIndex;
        private int _charsRead;
        private int _bytesRead;

        private StringBuilder _builder;

        private bool _isBlocked;

        public HttpRequestStreamReader(Stream stream, Encoding encoding)
            : this(stream, encoding, DefaultBufferSize)
        {
        }

        public HttpRequestStreamReader(Stream stream, Encoding encoding, int bufferSize)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (!stream.CanRead)
            {
                throw new ArgumentException(ICanHasResource("Argument_StreamNotReadable"), nameof(stream));
            }

            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            if (bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferSize));
            }

            _stream = stream;
            _encoding = encoding;
            _decoder = encoding.GetDecoder();

            if (bufferSize < MinBufferSize)
            {
                bufferSize = MinBufferSize;
            }

            _byteBuffer = new byte[bufferSize];
            var maxCharsPerBuffer = encoding.GetMaxCharCount(bufferSize);
            _charBuffer = new char[maxCharsPerBuffer];
        }

        public bool EndOfStream
        {
            get
            {
                if (_stream == null)
                {
                    throw new ObjectDisposedException("stream");
                }

                if (_charBufferIndex < _charsRead)
                {
                    return false;
                }

                var charsRead = ReadIntoBuffer();
                return charsRead == 0;
            }
        }

        public override void Close()
        {
            Dispose(true);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        public override int Peek()
        {
            if (_stream == null)
            {
                throw new ObjectDisposedException("stream");
            }

            if (_charBufferIndex == _charsRead)
            {
                if (_isBlocked || ReadIntoBuffer() == 0)
                {
                    return -1;
                }
            }

            return _charBuffer[_charBufferIndex];
        }

        public override int Read()
        {
            if (_stream == null)
            {
                throw new ObjectDisposedException("stream");
            }

            if (_charBufferIndex == _charsRead)
            {
                if (ReadIntoBuffer() == 0)
                {
                    return -1;
                }
            }

            return _charBuffer[_charBufferIndex++];
        }

        public override int Read(char[] buffer, int index, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (count < 0 || index + count >= buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (_stream == null)
            {
                throw new ObjectDisposedException("stream");
            }

            var charsRead = 0;
            while (count > 0)
            {
                var charsRemaining = _charsRead - _charBufferIndex;
                if (charsRemaining == 0)
                {
                    charsRemaining = ReadIntoBuffer();
                }

                if (charsRemaining == 0)
                {
                    break;  // We're at EOF
                }

                if (charsRemaining > count)
                {
                    charsRemaining = count;
                }

                Buffer.BlockCopy(_charBuffer, _charBufferIndex * 2, buffer, (index + charsRead) * 2, charsRemaining * 2);
                _charBufferIndex += charsRemaining;

                charsRead += charsRemaining;
                count -= charsRemaining;

                // If we got back fewer chars than we asked for, then it's likely the underlying stream is blocked.
                // Send the data back to the caller so they can process it.
                if (_isBlocked)
                {
                    break;
                }
            }

            return charsRead;
        }

        public override string ReadToEnd()
        {
            if (_stream == null)
            {
                throw new ObjectDisposedException("stream");
            }

            // Use a shared/cached StringBuilder instance ReadLine and ReadToEnd.
            var builder = AcquireSharedStringBuilder(_charsRead - _charBufferIndex);

            do
            {
                builder.Append(_charBuffer, _charBufferIndex, _charsRead - _charBufferIndex);
                _charBufferIndex = _charsRead;
                ReadIntoBuffer();
            }
            while (_charsRead > 0);

            return GetStringAndReleaseSharedStringBuilder(builder);
        }

        public override int ReadBlock(char[] buffer, int index, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (count < 0 || index + count >= buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (_stream == null)
            {
                throw new ObjectDisposedException("stream");
            }

            return base.ReadBlock(buffer, index, count);
        }

        private StringBuilder AcquireSharedStringBuilder(int capacity)
        {
            // Do not touch the shared builder if it will be removed on release
            if (capacity > MaxSharedBuilderCapacity)
            {
                return new StringBuilder(capacity);
            }

            // note that since StreamReader does not support concurrent reads it is not needed to
            // set _builder to null to avoid parallel acquisitions.
            var builder = _builder;
            if (builder == null)
            {
                return _builder = new StringBuilder(capacity);
            }

            // Clear the shared builder. Does not remove the allocated buffers so they are reused.
            builder.Length = 0;

            // When needed, recreate the buffer backing the StringBuilder so that further Append calls
            // are less likely to internally allocate new StringBuilders (or chunks).
            if (builder.Capacity < capacity)
            {
                builder.Capacity = capacity;
            }

            return builder;
        }

        private string GetStringAndReleaseSharedStringBuilder(StringBuilder builder)
        {
            if (builder == _builder && builder.Capacity > MaxSharedBuilderCapacity)
            {
                _builder = null;
            }

            return builder.ToString();
        }

        private int ReadIntoBuffer()
        {
            _charsRead = 0;
            _charBufferIndex = 0;
            _bytesRead = 0;

            do
            {
                _bytesRead = _stream.Read(_byteBuffer, 0, _byteBuffer.Length);
                if (_bytesRead == 0)  // We're at EOF
                {
                    return _charsRead;
                }

                _isBlocked = (_bytesRead < _byteBuffer.Length);
                _charsRead += _decoder.GetChars(_byteBuffer, 0, _bytesRead, _charBuffer, _charsRead);
            }
            while (_charsRead == 0);
            return _charsRead;
        }

        public override string ReadLine()
        {
            if (_stream == null)
            {
                throw new ObjectDisposedException("stream");
            }

            if (_charBufferIndex == _charsRead)
            {
                if (ReadIntoBuffer() == 0)
                {
                    return null;
                }
            }

            StringBuilder builder = null;
            do
            {
                int i = _charBufferIndex;
                do
                {
                    char ch = _charBuffer[i];
                    // Note the following common line feed chars:
                    // \n - UNIX   \r\n - DOS   \r - Mac
                    if (ch == '\r' || ch == '\n')
                    {
                        string result;
                        if (builder != null)
                        {
                            builder.Append(_charBuffer, _charBufferIndex, i - _charBufferIndex);
                            result = GetStringAndReleaseSharedStringBuilder(builder);
                        }
                        else
                        {
                            result = new string(_charBuffer, _charBufferIndex, i - _charBufferIndex);
                        }

                        _charBufferIndex = i + 1;
                        if (ch == '\r' && (_charBufferIndex < _charsRead || ReadIntoBuffer() > 0))
                        {
                            if (_charBuffer[_charBufferIndex] == '\n') _charBufferIndex++;
                        }

                        return result;
                    }
                    i++;
                }
                while (i < _charsRead);

                i = _charsRead - _charBufferIndex;

                if (builder == null)
                {
                    builder = AcquireSharedStringBuilder(i + 80);
                }
                builder.Append(_charBuffer, _charBufferIndex, i);
            }
            while (ReadIntoBuffer() > 0);

            return GetStringAndReleaseSharedStringBuilder(builder);
        }

        public override Task<string> ReadLineAsync()
        {
            if (_stream == null)
            {
                throw new ObjectDisposedException("stream");
            }

            var task = ReadLineAsyncInternal();
            return task;
        }

        private async Task<string> ReadLineAsyncInternal()
        {
            if (CharPos_Prop == CharLen_Prop && (await ReadIntoBufferAsync().ConfigureAwait(false)) == 0)
                return null;

            StringBuilder builder = null;

            do
            {
                char[] tmpCharBuffer = CharBuffer_Prop;
                int tmpCharLen = CharLen_Prop;
                int tmpCharPos = CharPos_Prop;
                int i = tmpCharPos;

                do
                {
                    char ch = tmpCharBuffer[i];

                    // Note the following common line feed chars:
                    // \n - UNIX   \r\n - DOS   \r - Mac
                    if (ch == '\r' || ch == '\n')
                    {
                        String s;

                        if (builder != null)
                        {
                            builder.Append(tmpCharBuffer, tmpCharPos, i - tmpCharPos);
                            s = GetStringAndReleaseSharedStringBuilder(builder);
                        }
                        else
                        {
                            s = new String(tmpCharBuffer, tmpCharPos, i - tmpCharPos);
                        }

                        CharPos_Prop = tmpCharPos = i + 1;

                        if (ch == '\r' && (tmpCharPos < tmpCharLen || (await ReadIntoBufferAsync().ConfigureAwait(false)) > 0))
                        {
                            tmpCharPos = CharPos_Prop;
                            if (CharBuffer_Prop[tmpCharPos] == '\n')
                                CharPos_Prop = ++tmpCharPos;
                        }

                        return s;
                    }

                    i++;

                }
                while (i < tmpCharLen);

                i = tmpCharLen - tmpCharPos;
                if (builder == null) builder = AcquireSharedStringBuilder(i + 80);
                builder.Append(tmpCharBuffer, tmpCharPos, i);

            }
            while (await ReadIntoBufferAsync().ConfigureAwait(false) > 0);

            return GetStringAndReleaseSharedStringBuilder(builder);
        }

        public override Task<string> ReadToEndAsync()
        {
            if (_stream == null)
            {
                throw new ObjectDisposedException("stream");
            }

            var task = ReadToEndAsyncInternal();
            return task;
        }

        private async Task<String> ReadToEndAsyncInternal()
        {
            var builder = AcquireSharedStringBuilder(CharLen_Prop - CharPos_Prop);
            do
            {
                int tmpCharPos = CharPos_Prop;
                builder.Append(CharBuffer_Prop, tmpCharPos, CharLen_Prop - tmpCharPos);
                CharPos_Prop = CharLen_Prop;  // We consumed these characters
                await ReadIntoBufferAsync().ConfigureAwait(false);
            } while (CharLen_Prop > 0);

            return GetStringAndReleaseSharedStringBuilder(builder);
        }

        public override Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (count < 0 || index + count >= buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (_stream == null)
            {
                throw new ObjectDisposedException("stream");
            }

            var task = ReadAsyncInternal(buffer, index, count);
            return task;
        }

        private async Task<int> ReadAsyncInternal(char[] buffer, int index, int count)
        {
            if (CharPos_Prop == CharLen_Prop && (await ReadIntoBufferAsync().ConfigureAwait(false)) == 0)
            {
                return 0;
            }

            var charsRead = 0;
            Stream tmpStream = Stream_Prop;

            while (count > 0)
            {
                // n is the characters available in _charBuffer
                int n = CharLen_Prop - CharPos_Prop;

                // charBuffer is empty, let's read from the stream
                if (n == 0)
                {
                    CharLen_Prop = 0;
                    CharPos_Prop = 0;
                    ByteLen_Prop = 0;

                    // We loop here so that we read in enough bytes to yield at least 1 char.
                    // We break out of the loop if the stream is blocked (EOF is reached).
                    do
                    {
                        Debug.Assert(n == 0);
                        ByteLen_Prop = await tmpStream.ReadAsync(_byteBuffer, 0, _byteBuffer.Length).ConfigureAwait(false);
                        if (ByteLen_Prop == 0)  // EOF
                        {
                            IsBlocked_Prop = true;
                            break;
                        }

                        // _isBlocked == whether we read fewer bytes than we asked for.
                        // Note we must check it here because CompressBuffer or 
                        // DetectEncoding will change _byteLen.
                        IsBlocked_Prop = (ByteLen_Prop < _byteBuffer.Length);

                        Contract.Assert(n == 0);

                        CharPos_Prop = 0;
                        n = Decoder_Prop.GetChars(_byteBuffer, 0, ByteLen_Prop, CharBuffer_Prop, 0);

                        // Why did the bytes yield no chars?
                        Contract.Assert(n > 0);

                        CharLen_Prop += n;  // Number of chars in StreamReader's buffer.

                    }
                    while (n == 0);

                    if (n == 0) break;  // We're at EOF
                }  // if (n == 0)

                // Got more chars in charBuffer than the user requested
                if (n > count)
                {
                    n = count;
                }

                Buffer.BlockCopy(CharBuffer_Prop, CharPos_Prop * 2, buffer, (index + charsRead) * 2, n * 2);
                CharPos_Prop += n;

                charsRead += n;
                count -= n;

                // This function shouldn't block for an indefinite amount of time,
                // or reading from a network stream won't work right.  If we got
                // fewer bytes than we requested, then we want to break right here.
                if (IsBlocked_Prop)
                {
                    break;
                }
            }

            return charsRead;
        }

        public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (count < 0 || index + count >= buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (_stream == null)
            {
                throw new ObjectDisposedException("stream");
            }

            return base.ReadBlockAsync(buffer, index, count);
        }

        private Int32 CharLen_Prop
        {
            get { return _charsRead; }
            set { _charsRead = value; }
        }

        private Int32 CharPos_Prop
        {
            get { return _charBufferIndex; }
            set { _charBufferIndex = value; }
        }

        private Int32 ByteLen_Prop
        {
            get { return _bytesRead; }
            set { _bytesRead = value; }
        }

        private Decoder Decoder_Prop
        {
            get { return _decoder; }
        }

        private Char[] CharBuffer_Prop
        {
            get { return _charBuffer; }
        }

        private Byte[] ByteBuffer_Prop
        {
            get { return _byteBuffer; }
        }

        private bool IsBlocked_Prop
        {
            get { return _isBlocked; }
            set { _isBlocked = value; }
        }

        private Stream Stream_Prop
        {
            get { return _stream; }
        }

        private async Task<int> ReadIntoBufferAsync()
        {
            CharLen_Prop = 0;
            CharPos_Prop = 0;
            ByteLen_Prop = 0;

            Byte[] tmpByteBuffer = ByteBuffer_Prop;
            Stream tmpStream = Stream_Prop;

            do
            {

                ByteLen_Prop = await tmpStream.ReadAsync(tmpByteBuffer, 0, tmpByteBuffer.Length).ConfigureAwait(false);
                Debug.Assert(ByteLen_Prop >= 0, "Stream.Read returned a negative number!  Bug in stream class.");

                if (ByteLen_Prop == 0)  // We're at EOF
                    return CharLen_Prop;

                // _isBlocked == whether we read fewer bytes than we asked for.
                // Note we must check it here because CompressBuffer or 
                // DetectEncoding will change _byteLen.
                IsBlocked_Prop = (ByteLen_Prop < tmpByteBuffer.Length);

                CharLen_Prop += Decoder_Prop.GetChars(tmpByteBuffer, 0, ByteLen_Prop, CharBuffer_Prop, CharLen_Prop);
            }
            while (CharLen_Prop == 0);

            return CharLen_Prop;
        }

        private static string ICanHasResource(string s)
        {
            return s;
        }
    }
}