using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;


namespace vim
{
    /// <summary>
    /// xxHash implementation
    /// </summary>
    public static class Hash
    {
        #region Static buffer

        [ThreadStatic]
        private static byte[] _buffer;
        private static byte[] InitBuffer(long length)
        {
            if (_buffer == null || _buffer.Length < length)
                _buffer = new byte[Math.Max(MinimalBufferSize, length)];
            return _buffer;
        }

        // Used in default Stream xxHash implementation
        // NOTE: Must be multiple of 32 (== 64-bit xxHash stripe length)
        internal const int DefaultXXHashBufferSize = 16 * 1024;
        internal const int MinimalBufferSize = 64000;

        #endregion // Static buffer

        #region Old Hash interface

        /// <summary>
        /// Calculate 64-bit xxHash for given file
        /// </summary>
        /// <param name="fileSystemFullName"></param>
        /// <param name="len"></param>
        /// <param name="buff"></param>
        /// <param name="rethrow">If true, any exception during calculation will be rethrown</param>
        /// <param name="seed"></param>
        /// <returns></returns>
        public static ulong file(string fileSystemFullName, long len = -1, byte[] buff = null, bool rethrow = false, ulong seed = 0)
        {
            var _f = new FileInfo(fileSystemFullName);
            var l = _f.Length;
            if (l == 0)
                return 0;

            if (len == -1)
            {
                if (buff == null)
                {
                    InitBuffer(DefaultXXHashBufferSize);
                    ulong file_hash = 0;
                    vib.Utils.TryIO(() => {
                        // default stream xxhash implementation
                        using (var f = new FileStream(fileSystemFullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                            file_hash = Hash64(f, _buffer, seed);
                        }
                    }, "Hash.file", rethrow);
                    return file_hash;
                }

                len = l;
            }

            byte[] buf = buff ?? InitBuffer(DefaultXXHashBufferSize);
            var hash = Reset(seed);

            var res = vib.Utils.TryIO(() => {
                using (var f = new FileStream(fileSystemFullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    while (len > 0)
                    {
                        int bytesToRead = buf.Length;
                        if (bytesToRead > len)
                            bytesToRead = (int)len;

                        f.Read(buf, 0, bytesToRead);
                        hash.Update(buf, bytesToRead);
                        len -= bytesToRead;
                    }
                }
            }, "Hash.file", rethrow);

            return res.IsOk ? hash.Get() : 0;
        }

        public static ulong Long(byte[] input, int len, ulong seed = 0)
        {
            if (input == null || input.Length == 0)
                return 0;

            if (len == -1)
                len = input.Length;

            unsafe
            {
                fixed (byte* buffer = input)
                {
                    return Hash64(buffer, len, seed);
                }
            }
        }

        public static ulong Long(string input, ulong seed = 0)
        {
            if (string.IsNullOrEmpty(input))
                return 0;

            var charsCount = input.Length;
            var bytesCount = Encoding.UTF8.GetMaxByteCount(charsCount);
            InitBuffer(bytesCount);
            var writtenBytesCount = Encoding.UTF8.GetBytes(input, 0, charsCount, _buffer, 0);
            return Long(_buffer, writtenBytesCount, seed);
        }

        public static ulong Long<T>(T input, ulong seed = 0) where T : unmanaged
        {
            unsafe
            {
                return Hash64((byte*)&input, sizeof(T), seed);
            }
        }

        public static ulong Long(byte[] input, ulong seed = 0) => Long(input, input.Length, seed);

        public static ulong Long<T>(T[] input, ulong seed = 0) where T : unmanaged
        {
            if (input == null || input.Length == 0)
                return seed;

            unsafe
            {
                fixed (T* data = input)
                {
                    return Hash64((byte*)&data, input.Length * sizeof(T), seed);
                }
            }
        }

        public static uint Int(byte[] input, int len, uint seed = 0)
        {
            if (input == null || input.Length == 0)
                return seed;

            if (len == -1)
                len = input.Length;

            unsafe
            {
                fixed (byte* buffer = input)
                {
                    return Hash32(buffer, len, seed);
                }
            }
        }

        public static uint Int(string input, uint seed = 0)
        {
            if (string.IsNullOrEmpty(input))
                return seed;

            var charsCount = input.Length;
            var bytesCount = Encoding.UTF8.GetMaxByteCount(charsCount);
            InitBuffer(bytesCount);
            var writtenBytesCount = Encoding.UTF8.GetBytes(input, 0, charsCount, _buffer, 0);
            return Int(_buffer, writtenBytesCount, seed);
        }


        public static uint Int<T>(T input, uint seed = 0) where T : unmanaged
        {
            unsafe
            {
                return Hash32((byte*)&input, sizeof(T), seed);
            }
        }

        public static uint Int<T>(T[] input, uint seed = 0) where T : unmanaged
        {
            if (input == null || input.Length == 0)
                return 0;

            unsafe
            {
                fixed (T* data = input)
                {
                    return Hash32((byte*)&data, input.Length * sizeof(T), seed);
                }
            }
        }

        public static void ProcessHashTokens(string str, Action<uint> action)
        {
            if (String.IsNullOrEmpty(str))
                return;

            var splitted = str.Split(' ');
            foreach (var entry in splitted)
            {
                var hash = Int(entry);
                action(hash);
            }
        }

        #endregion // Old Hash interface

        #region Hash32

        #region Constants

        private const uint prime32v1 = 2654435761u;
        private const uint prime32v2 = 2246822519u;
        private const uint prime32v3 = 3266489917u;
        private const uint prime32v4 = 668265263u;
        private const uint prime32v5 = 374761393u;
        private const int stripeLength32 = 16;
        private const int readBufferSize32 = Hash.DefaultXXHashBufferSize; // has to be stripe aligned

        #endregion // Constants

        #region State

        /// <summary>
        /// 32-bit xxHash state used for continous calculation of xxHash over several sources
        /// </summary>
        public unsafe struct State32
        {
            internal uint total_len_32;
            internal uint acc1;
            internal uint acc2;
            internal uint acc3;
            internal uint acc4;
            internal fixed uint mem32[4];
            internal uint memsize;
            internal bool large_len;

            #region Methods

            public void Update(string input)
            {
                if (string.IsNullOrEmpty(input))
                    return;

                var charsCount = input.Length;
                var bytesCount = Encoding.UTF8.GetMaxByteCount(charsCount);
                InitBuffer(bytesCount);
                var writtenBytesCount = Encoding.UTF8.GetBytes(input, 0, charsCount, _buffer, 0);
                fixed (byte* data = _buffer)
                {
                    Update32(data, ref this, (uint)writtenBytesCount);
                }
            }

            public void Update<T>(T input) where T : unmanaged {
                Update32((byte*)&input, ref this, (uint)sizeof(T));
            }

            public void Update(byte[] input, int len = -1)
            {
                if (input == null || input.Length == 0)
                    return;

                if (len == -1)
                    len = input.Length;

                fixed (byte* data = input)
                {
                    Update32(data, ref this, (uint)len);
                }
            }

            /// <summary>
            /// Calculate actual xxHash value for given State
            /// </summary>
            /// <returns>32-bit hash</returns>
            public uint Get()
            {
                uint h32;
                if (this.large_len)
                {
                    h32 = Bits.RotateLeft(this.acc1, 1)
                        + Bits.RotateLeft(this.acc2, 7)
                        + Bits.RotateLeft(this.acc3, 12)
                        + Bits.RotateLeft(this.acc4, 18);
                }
                else
                {
                    h32 = this.acc3 /* == seed */ + prime32v5;
                }

                h32 += this.total_len_32;

                fixed (uint* byteMem = this.mem32)
                {
                    return Finalize32(h32, (byte*)byteMem, this.memsize, Bits.Alignment.Aligned);
                }
            }

            #endregion // Methods
        }

        #endregion // State

        #region Hash implementation interface

        /// <summary>
        /// Create new 32-bit hash state that allows to do multiple
        /// Update() calls before actual hash calculation
        /// </summary>
        /// <param name="seed"></param>
        /// <returns></returns>
        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static State32 Reset32(uint seed = 0)
        {
            return new State32
            {
                acc1 = seed + prime32v1 + prime32v2,
                acc2 = seed + prime32v2,
                acc3 = seed,
                acc4 = seed - prime32v1,
            };
        }

        internal static unsafe void Update32(byte* input, ref State32 state, uint len)
        {
            fixed (uint* mem32 = state.mem32)
            {

                byte* p = input;
                byte* bEnd = p + len;
                byte* bmem32 = (byte*)mem32;

                state.total_len_32 += len;
                state.large_len |= ((len >= 16) | (state.total_len_32 >= 16));

                if (state.memsize + len < 16)
                {   /* fill in tmp buffer */
                    Bits.MemCpy(bmem32 + state.memsize, input, len);
                    state.memsize += len;
                    return;
                    // return XXH_OK;
                }

                if (state.memsize > 0)
                {   /* some data left from previous update */
                    Bits.MemCpy(bmem32 + state.memsize, input, (ulong)(16 - state.memsize));
                    {
                        uint* p32 = mem32;
                        state.acc1 = round32(state.acc1, Bits.XXH_readLE32((byte*)p32)); p32++;
                        state.acc2 = round32(state.acc2, Bits.XXH_readLE32((byte*)p32)); p32++;
                        state.acc3 = round32(state.acc3, Bits.XXH_readLE32((byte*)p32)); p32++;
                        state.acc4 = round32(state.acc4, Bits.XXH_readLE32((byte*)p32));
                    }
                    p += 16 - state.memsize;
                    state.memsize = 0;
                }

                if (p <= bEnd - 16)
                {
                    byte* limit = bEnd - 16;
                    uint v1 = state.acc1;
                    uint v2 = state.acc2;
                    uint v3 = state.acc3;
                    uint v4 = state.acc4;

                    do
                    {
                        v1 = round32(v1, Bits.XXH_readLE32(p)); p += 4;
                        v2 = round32(v2, Bits.XXH_readLE32(p)); p += 4;
                        v3 = round32(v3, Bits.XXH_readLE32(p)); p += 4;
                        v4 = round32(v4, Bits.XXH_readLE32(p)); p += 4;
                    } while (p <= limit);

                    state.acc1 = v1;
                    state.acc2 = v2;
                    state.acc3 = v3;
                    state.acc4 = v4;
                }

                if (p < bEnd)
                {
                    Bits.MemCpy(bmem32, p, (ulong)(bEnd - p));
                    state.memsize = (uint)(bEnd - p);
                }
            }
        }


        internal static unsafe uint Hash32(byte* buffer, int len, uint seed = 0)
        {
            bool bigEndian = Bits.IsBigEndian;

            int remainingLen = len;
            uint acc;

            if (len >= stripeLength32)
            {
                var state = Reset32(seed);
                do
                {
                    acc = processStripe32(ref buffer, ref state.acc1, ref state.acc2, ref state.acc3, ref state.acc4, bigEndian);
                    remainingLen -= stripeLength32;
                }
                while (remainingLen >= stripeLength32);
            }
            else
            {
                acc = seed + prime32v5;
            }

            acc += (uint)len;
            acc = processRemaining32(buffer, acc, remainingLen, bigEndian);

            return avalanche32(acc);
        }

        internal static unsafe uint Hash32(Stream stream, byte[] buffer, uint seed = 0)
        {
            bool bigEndian = Bits.IsBigEndian;
            uint acc;

            int readBytes = stream.Read(buffer, 0, readBufferSize32);
            int len = readBytes;

            fixed (byte* inputPtr = buffer)
            {
                byte* pInput = inputPtr;
                if (readBytes >= stripeLength32)
                {
                    var state = Reset32(seed);
                    do
                    {
                        do
                        {
                            acc = processStripe32(
                                ref pInput,
                                ref state.acc1,
                                ref state.acc2,
                                ref state.acc3,
                                ref state.acc4,
                                bigEndian);
                            readBytes -= stripeLength32;
                        }
                        while (readBytes >= stripeLength32);

                        // read more if the alignment is still intact
                        if (readBytes == 0)
                        {
                            readBytes = stream.Read(buffer, 0, readBufferSize32);
                            pInput = inputPtr;
                            len += readBytes;
                        }
                    }
                    while (readBytes >= stripeLength32);
                }
                else
                {
                    acc = seed + prime32v5;
                }

                acc += (uint)len;
                acc = processRemaining32(pInput, acc, readBytes, bigEndian);
            }

            return avalanche32(acc);
        }

        #endregion // Hash implementation interface

        #region Helper functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe uint Finalize32(uint h32, byte* ptr, long len, Bits.Alignment align)
        {
            // Compact preroll version
            len &= 15;
            while (len >= 4)
            {
                h32 += Bits.GetBits32(ptr, align) * prime32v3;
                ptr += 4;
                h32 = Bits.RotateLeft(h32, 17) * prime32v4;
                len -= 4;
            }
            while (len > 0)
            {
                h32 += (*ptr++) * prime32v5; h32 = Bits.RotateLeft(h32, 11) * prime32v1;
                --len;
            }
            return avalanche32(h32);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe uint processStripe32(
            ref byte* pInput,
            ref uint acc1,
            ref uint acc2,
            ref uint acc3,
            ref uint acc4,
            bool bigEndian)
        {
            if (bigEndian)
            {
                processLaneBigEndian32(ref pInput, ref acc1);
                processLaneBigEndian32(ref pInput, ref acc2);
                processLaneBigEndian32(ref pInput, ref acc3);
                processLaneBigEndian32(ref pInput, ref acc4);
            }
            else
            {
                processLane32(ref pInput, ref acc1);
                processLane32(ref pInput, ref acc2);
                processLane32(ref pInput, ref acc3);
                processLane32(ref pInput, ref acc4);
            }

            return Bits.RotateLeft(acc1, 1)
                 + Bits.RotateLeft(acc2, 7)
                 + Bits.RotateLeft(acc3, 12)
                 + Bits.RotateLeft(acc4, 18);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void processLane32(ref byte* pInput, ref uint accn)
        {
            uint lane = *(uint*)pInput;
            accn = round32(accn, lane);
            pInput += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void processLaneBigEndian32(ref byte* pInput, ref uint accn)
        {
            uint lane = Bits.SwapBytes32(*(uint*)pInput);
            accn = round32(accn, lane);
            pInput += 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe uint processRemaining32(
            byte* pInput,
            uint acc,
            int remainingLen,
            bool bigEndian)
        {
            for (uint lane; remainingLen >= 4; remainingLen -= 4, pInput += 4)
            {
                lane = *(uint*)pInput;
                if (bigEndian)
                {
                    lane = Bits.SwapBytes32(lane);
                }

                acc += lane * prime32v3;
                acc = Bits.RotateLeft(acc, 17) * prime32v4;
            }

            for (byte lane; remainingLen >= 1; remainingLen--, pInput++)
            {
                lane = *pInput;
                acc += lane * prime32v5;
                acc = Bits.RotateLeft(acc, 11) * prime32v1;
            }

            return acc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe uint round32(uint accn, uint lane)
        {
            accn += lane * prime32v2;
            accn = Bits.RotateLeft(accn, 13);
            accn *= prime32v1;
            return accn;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint avalanche32(uint acc)
        {
            acc ^= acc >> 15;
            acc *= prime32v2;
            acc ^= acc >> 13;
            acc *= prime32v3;
            acc ^= acc >> 16;
            return acc;
        }

        #endregion // Helper functions

        #endregion // Hash32

        #region Hash64

        #region Constants

        private const ulong prime64v1 = 11400714785074694791ul;
        private const ulong prime64v2 = 14029467366897019727ul;
        private const ulong prime64v3 = 1609587929392839161ul;
        private const ulong prime64v4 = 9650029242287828579ul;
        private const ulong prime64v5 = 2870177450012600261ul;
        private const int stripeLength64 = 32;
        private const int readBufferSize = Hash.DefaultXXHashBufferSize; // 16kb - has to be stripe aligned


        #endregion // Constants

        #region State

        /// <summary>
        /// 64-bit xxHash state used for continous calculation of xxHash over several sources
        /// </summary>
        public unsafe struct State64
        {
            internal ulong total_len;
            internal ulong acc1;
            internal ulong acc2;
            internal ulong acc3;
            internal ulong acc4;
            internal fixed ulong mem64[4];
            internal uint memsize;

            #region Methods

            public void Update<T>(T input) where T : unmanaged {
                Hash.Update((byte*)&input, ref this, (ulong)sizeof(T));
            }

            public void Update<T>(T* input, int len ) where T : unmanaged {
                Hash.Update((byte*)input, ref this, (ulong)(len * sizeof(T)));
            }

            public void Update<T>( T[] input ) where T : unmanaged {
                Update( input, input.Length );
            }

            public void Update<T>(T[] input, int length) where T : unmanaged
            {
                if (input == null || length == 0)
                    return;

                fixed (T* data = input)
                {
                    Hash.Update((byte*)data, ref this, (ulong)(length * sizeof(T)));
                }
            }

            /// <summary>
            /// This is a slow method!
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="input"></param>
            public void Update<T>( IReadOnlyList<T> input ) where T : unmanaged {
                if ( input == null || input.Count == 0 )
                    return;

                for ( int idx = 0, cnt = input.Count; idx < cnt; idx++ ) {
                    Update( input[idx] );
                }
            }

            /// <summary>
            /// This is a slow method!
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="input"></param>
            public void Update<T>( IEnumerable<T> input ) where T : unmanaged {
                if ( input == null || !input.Any() )
                    return;

                foreach ( var item in input ) {
                    Update( item );
                }
            }

            public void Update(string input)
            {
                if (string.IsNullOrEmpty(input))
                    return;

                var charsCount = input.Length;
                var bytesCount = Encoding.UTF8.GetMaxByteCount(charsCount);
                InitBuffer(bytesCount);
                var writtenBytesCount = Encoding.UTF8.GetBytes(input, 0, charsCount, _buffer, 0);
                
                fixed (byte* data = _buffer)
                {
                    Hash.Update(data, ref this, (ulong)writtenBytesCount);
                }
            }

            public void Update(byte[] input, int len = -1)
            {
                if (input == null || input.Length == 0)
                    return;

                if (len == -1)
                    len = input.Length;

                fixed (byte* data = input)
                {
                    Hash.Update(data, ref this, (ulong)len);
                }
            }

            /// <summary>
            /// Calculate actual xxHash value for given State
            /// </summary>
            /// <returns>64-bit hash</returns>
            public ulong Get()
            {
                ulong h64;

                if (this.total_len >= 32)
                {
                    ulong v1 = this.acc1;
                    ulong v2 = this.acc2;
                    ulong v3 = this.acc3;
                    ulong v4 = this.acc4;

                    h64 = Bits.RotateLeft(v1, 1) + Bits.RotateLeft(v2, 7) + Bits.RotateLeft(v3, 12) + Bits.RotateLeft(v4, 18);
                    mergeAccumulator64(ref h64, v1);
                    mergeAccumulator64(ref h64, v2);
                    mergeAccumulator64(ref h64, v3);
                    mergeAccumulator64(ref h64, v4);
                }
                else
                {
                    h64 = this.acc3 /*seed*/ + prime64v5;
                }

                h64 += (ulong)this.total_len;
                fixed (ulong* mem64 = this.mem64)
                {
                    return Finalize64(h64, (byte*)mem64, this.total_len, Bits.Alignment.Aligned);
                }
            }

            #endregion // Methods
        }

        #endregion // State

        #region Hash implementation interface

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe State64 Reset(ulong seed = 0)
        {
            return new State64
            {
                acc1 = seed + prime64v1 + prime64v2,
                acc2 = seed + prime64v2,
                acc3 = seed,
                acc4 = seed - prime64v1,
            };
        }

        internal static unsafe void Update(byte* input, ref State64 state, ulong len)
        {
            fixed (ulong* mem64 = state.mem64)
            {
                byte* p = input;
                byte* bEnd = p + len;

                state.total_len += (ulong)len;

                if (state.memsize + len < 32)
                {  /* fill in tmp buffer */
                    Bits.MemCpy(((byte*)mem64) + state.memsize, input, len);
                    state.memsize += (uint)len;
                    return;
                }

                if (state.memsize > 0)
                {   /* tmp buffer is full */
                    Bits.MemCpy(((byte*)mem64) + state.memsize, input, 32 - state.memsize);
                    state.acc1 = round64(state.acc1, Bits.XXH_readLE64((byte*)(mem64 + 0)));
                    state.acc2 = round64(state.acc2, Bits.XXH_readLE64((byte*)(mem64 + 1)));
                    state.acc3 = round64(state.acc3, Bits.XXH_readLE64((byte*)(mem64 + 2)));
                    state.acc4 = round64(state.acc4, Bits.XXH_readLE64((byte*)(mem64 + 3)));
                    p += 32 - state.memsize;
                    state.memsize = 0;
                }

                if (p + 32 <= bEnd)
                {
                    byte* limit = bEnd - 32;
                    ulong v1 = state.acc1;
                    ulong v2 = state.acc2;
                    ulong v3 = state.acc3;
                    ulong v4 = state.acc4;

                    do
                    {
                        v1 = round64(v1, Bits.XXH_readLE64(p)); p += 8;
                        v2 = round64(v2, Bits.XXH_readLE64(p)); p += 8;
                        v3 = round64(v3, Bits.XXH_readLE64(p)); p += 8;
                        v4 = round64(v4, Bits.XXH_readLE64(p)); p += 8;
                    } while (p <= limit);

                    state.acc1 = v1;
                    state.acc2 = v2;
                    state.acc3 = v3;
                    state.acc4 = v4;
                }

                if (p < bEnd)
                {
                    Bits.MemCpy((byte*)mem64, p, (ulong)(bEnd - p));
                    state.memsize = (uint)(bEnd - p);
                }
            }
        }

        public static unsafe ulong Hash64(byte* buffer, int len, ulong seed = 0)
        {
            bool bigEndian = Bits.IsBigEndian;

            int remainingLen = len;
            ulong acc;

            if (len >= stripeLength64)
            {
                var state = Reset(seed);
                do
                {
                    acc = processStripe64(ref buffer, ref state.acc1, ref state.acc2, ref state.acc3, ref state.acc4, bigEndian);
                    remainingLen -= stripeLength64;
                }
                while (remainingLen >= stripeLength64);
            }
            else
            {
                acc = seed + prime64v5;
            }

            acc += (ulong)len;
            acc = processRemaining64(buffer, acc, remainingLen, bigEndian);

            return avalanche64(acc);
        }

        public static unsafe ulong Hash64(Stream stream, byte[] buffer, ulong seed = 0)
        {
            bool bigEndian = Bits.IsBigEndian;

            ulong acc;

            int readBytes = stream.Read(buffer, 0, readBufferSize);
            ulong len = (ulong)readBytes;

            fixed (byte* inputPtr = buffer)
            {
                byte* pInput = inputPtr;
                if (readBytes >= stripeLength64)
                {
                    var state = Reset(seed);
                    do
                    {
                        do
                        {
                            acc = processStripe64(
                                ref pInput,
                                ref state.acc1,
                                ref state.acc2,
                                ref state.acc3,
                                ref state.acc4,
                                bigEndian);
                            readBytes -= stripeLength64;
                        }
                        while (readBytes >= stripeLength64);

                        // read more if the alignment is intact
                        if (readBytes == 0)
                        {
                            readBytes = stream.Read(buffer, 0, readBufferSize);
                            pInput = inputPtr;
                            len += (ulong)readBytes;
                        }
                    }
                    while (readBytes >= stripeLength64);
                }
                else
                {
                    acc = seed + prime64v5;
                }

                acc += len;
                acc = processRemaining64(pInput, acc, readBytes, bigEndian);
            }

            return avalanche64(acc);
        }

        #endregion // Hash implementation interface

        #region Helper functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe ulong Finalize64(ulong h64, byte* ptr, ulong len, Bits.Alignment align)
        {
            len &= 31;
            while (len >= 8)
            {
                ulong k1 = round64(0, Bits.GetBits64(ptr, align));
                ptr += 8;
                h64 ^= k1;
                h64 = Bits.RotateLeft(h64, 27) * prime64v1 + prime64v4;
                len -= 8;
            }
            if (len >= 4)
            {
                h64 ^= (ulong)(Bits.GetBits32(ptr, align)) * prime64v1;
                ptr += 4;
                h64 = Bits.RotateLeft(h64, 23) * prime64v2 + prime64v3;
                len -= 4;
            }
            while (len > 0)
            {
                h64 ^= (*ptr++) * prime64v5;
                h64 = Bits.RotateLeft(h64, 11) * prime64v1;
                --len;
            }
            return avalanche64(h64);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ulong processStripe64(ref byte* pInput, ref ulong acc1, ref ulong acc2, ref ulong acc3, ref ulong acc4, bool bigEndian)
        {
            if (bigEndian)
            {
                processLaneBigEndian64(ref acc1, ref pInput);
                processLaneBigEndian64(ref acc2, ref pInput);
                processLaneBigEndian64(ref acc3, ref pInput);
                processLaneBigEndian64(ref acc4, ref pInput);
            }
            else
            {
                processLane64(ref acc1, ref pInput);
                processLane64(ref acc2, ref pInput);
                processLane64(ref acc3, ref pInput);
                processLane64(ref acc4, ref pInput);
            }

            ulong acc = Bits.RotateLeft(acc1, 1)
                      + Bits.RotateLeft(acc2, 7)
                      + Bits.RotateLeft(acc3, 12)
                      + Bits.RotateLeft(acc4, 18);

            mergeAccumulator64(ref acc, acc1);
            mergeAccumulator64(ref acc, acc2);
            mergeAccumulator64(ref acc, acc3);
            mergeAccumulator64(ref acc, acc4);
            return acc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void processLane64(ref ulong accn, ref byte* pInput)
        {
            ulong lane = *(ulong*)pInput;
            accn = round64(accn, lane);
            pInput += 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void processLaneBigEndian64(ref ulong accn, ref byte* pInput)
        {
            ulong lane = *(ulong*)pInput;
            lane = Bits.SwapBytes64(lane);
            accn = round64(accn, lane);
            pInput += 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ulong processRemaining64(byte* pInput, ulong acc, int remainingLen, bool bigEndian)
        {
            for (ulong lane; remainingLen >= 8; remainingLen -= 8, pInput += 8)
            {
                lane = *(ulong*)pInput;
                if (bigEndian)
                {
                    lane = Bits.SwapBytes64(lane);
                }

                acc ^= round64(0, lane);
                acc = Bits.RotateLeft(acc, 27) * prime64v1;
                acc += prime64v4;
            }

            for (uint lane32; remainingLen >= 4; remainingLen -= 4, pInput += 4)
            {
                lane32 = *(uint*)pInput;
                if (bigEndian)
                {
                    lane32 = Bits.SwapBytes32(lane32);
                }

                acc ^= lane32 * prime64v1;
                acc = Bits.RotateLeft(acc, 23) * prime64v2;
                acc += prime64v3;
            }

            for (byte lane8; remainingLen >= 1; remainingLen--, pInput++)
            {
                lane8 = *pInput;
                acc ^= lane8 * prime64v5;
                acc = Bits.RotateLeft(acc, 11) * prime64v1;
            }

            return acc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong avalanche64(ulong acc)
        {
            acc ^= acc >> 33;
            acc *= prime64v2;
            acc ^= acc >> 29;
            acc *= prime64v3;
            acc ^= acc >> 32;
            return acc;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong round64(ulong accn, ulong lane)
        {
            accn += lane * prime64v2;
            return Bits.RotateLeft(accn, 31) * prime64v1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void mergeAccumulator64(ref ulong acc, ulong accn)
        {
            acc ^= round64(0, accn);
            acc *= prime64v1;
            acc += prime64v4;
        }

        #endregion // Helper functions

        #endregion // Hash64

    }

    /// <summary>
    /// Bit operations.
    /// </summary>
    internal static class Bits
    {
        internal enum Alignment { Aligned, Unaligned }

#pragma warning disable SA1401 // Fields should be private - this isn't publicly exposed
        internal static bool IsBigEndian = !BitConverter.IsLittleEndian;
#pragma warning restore SA1401 // Fields should be private

        #region Operations over bits

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong RotateLeft(ulong value, int bits)
        {
            return (value << bits) | (value >> (64 - bits));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint RotateLeft(uint value, int bits)
        {
            return (value << bits) | (value >> (32 - bits));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint RotateRight(uint value, int bits)
        {
            return (value >> bits) | (value << (32 - bits));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong RotateRight(ulong value, int bits)
        {
            return (value >> bits) | (value << (64 - bits));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe ulong PartialBytesToUInt64(byte* ptr, int leftBytes)
        {
            // a switch/case approach is slightly faster than the loop but .net
            // refuses to inline it due to larger code size.
            ulong result = 0;

            // trying to modify leftBytes would invalidate inlining
            // need to use local variable instead
            for (int i = 0; i < leftBytes; i++)
            {
                result |= ((ulong)ptr[i]) << (i << 3);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe ulong PartialBytesToUInt64(byte[] buffer, int leftBytes)
        {
            // a switch/case approach is slightly faster than the loop but .net
            // refuses to inline it due to larger code size.
            ulong result = 0;

            // trying to modify leftBytes would invalidate inlining
            // need to use local variable instead
            for (int i = 0; i < leftBytes; i++)
            {
                result |= ((ulong)buffer[i]) << (i << 3);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe uint PartialBytesToUInt32(byte* ptr, int leftBytes)
        {
            if (leftBytes > 3)
            {
                return *((uint*)ptr);
            }

            // a switch/case approach is slightly faster than the loop but .net
            // refuses to inline it due to larger code size.
            uint result = *ptr;
            if (leftBytes > 1)
            {
                result |= (uint)(ptr[1] << 8);
            }

            if (leftBytes > 2)
            {
                result |= (uint)(ptr[2] << 16);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe uint PartialBytesToUInt32(byte[] buffer, int leftBytes)
        {
            if (leftBytes > 3)
            {
                return BitConverter.ToUInt32(buffer, 0);
            }

            // a switch/case approach is slightly faster than the loop but .net
            // refuses to inline it due to larger code size.
            uint result = buffer[0];
            if (leftBytes > 1)
            {
                result |= (uint)(buffer[1] << 8);
            }

            if (leftBytes > 2)
            {
                result |= (uint)(buffer[2] << 16);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint SwapBytes32(uint num)
        {
            return (Bits.RotateLeft(num, 8) & 0x00FF00FFu)
                 | (Bits.RotateRight(num, 8) & 0xFF00FF00u);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong SwapBytes64(ulong num)
        {
            num = (Bits.RotateLeft(num, 48) & 0xFFFF0000FFFF0000ul)
                | (Bits.RotateLeft(num, 16) & 0x0000FFFF0000FFFFul);
            return (Bits.RotateLeft(num, 8) & 0xFF00FF00FF00FF00ul)
                 | (Bits.RotateRight(num, 8) & 0x00FF00FF00FF00FFul);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe uint GetBits32(byte* ptr, Bits.Alignment align)
        {
            if (align == Bits.Alignment.Unaligned)
            {
                return XXH_readLE32(ptr);
            }
            else
            {
                return IsBigEndian
                    ? SwapBytes32(*(uint*)ptr)
                    : *(uint*)ptr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe uint XXH_readLE32(byte* bytePtr)
        {
            return bytePtr[0]
                | ((uint)bytePtr[1] << 8)
                | ((uint)bytePtr[2] << 16)
                | ((uint)bytePtr[3] << 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe byte* MemCpy(byte* dest, byte* src, ulong size)
        {
            for (uint i = 0; i < size; i++)
            {
                dest[i] = src[i];
            }
            return dest;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe ulong GetBits64(byte* ptr, Alignment align)
        {
            if (align == Alignment.Unaligned)
                return XXH_readLE64(ptr);
            else
                return IsBigEndian
                    ? SwapBytes64(*(ulong*)ptr)
                    : *(ulong*)ptr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe ulong XXH_readLE64(byte* bytePtr)
        {
            return bytePtr[0]
                | ((ulong)bytePtr[1] << 8)
                | ((ulong)bytePtr[2] << 16)
                | ((ulong)bytePtr[3] << 24)
                | ((ulong)bytePtr[4] << 32)
                | ((ulong)bytePtr[5] << 40)
                | ((ulong)bytePtr[6] << 48)
                | ((ulong)bytePtr[7] << 56);
        }

        #endregion // Operations over bits
    }
}
