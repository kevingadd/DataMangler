﻿using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq.Expressions;

namespace Squared.Data.Mangler.Internal {
    delegate SafeBuffer GetSafeBufferFunc (UnmanagedMemoryAccessor accessor);
    delegate Int64 GetPointerOffsetFunc (MemoryMappedViewAccessor accessor);

    public static class InternalExtensions {
        private static readonly GetSafeBufferFunc _GetSafeBuffer;
        private static readonly GetPointerOffsetFunc _GetPointerOffset;

        static InternalExtensions () {
            _GetSafeBuffer = CreateGetSafeBuffer();
            _GetPointerOffset = CreateGetPointerOffset();
        }

        // To manipulate structures directly in mapped memory, we have
        //  to be able to get a pointer to the mapping. While this is possible,
        //  the classes for using mapped files do not expose a way to do this
        //  directly. So, we pull out the SafeBuffer object associated with the
        //  mapping and then use a public method to get a pointer.
        // Kind of nasty, but what else can you do?
        private static GetSafeBufferFunc CreateGetSafeBuffer () {
            var t = typeof(UnmanagedMemoryAccessor);
            var field = t.GetField(
                "_buffer",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance
            );
            if (field == null)
                throw new ArgumentNullException();

            var argument = Expression.Parameter(t, "accessor");
            var expr = Expression.Field(argument, field);

            return Expression.Lambda<GetSafeBufferFunc>(
                expr, "GetSafeBuffer", new[] { argument }
            ).Compile();
        }

        // When we get a pointer from a SafeBuffer associated with a mapped
        //  view, the pointer is going to be wrong unless the offset into
        //  the file that we mapped was aligned with a page boundary.
        // So, once we get the pointer, we have to find out the alignment
        //  necessary to line it up with a page, and add that to the pointer
        //  so that we are looking at the start of the mapping, instead of
        //  the start of the page containing the mapping.
        // This is a bit messier than the SafeBuffer hack, because one of the
        //  relevant types - MemoryMappedView - is internal.
        private static GetPointerOffsetFunc CreateGetPointerOffset () {
            var tAccessor = typeof(MemoryMappedViewAccessor);
            var tView = tAccessor.Assembly.GetType(
                "System.IO.MemoryMappedFiles.MemoryMappedView", true
            );

            var fieldView = tAccessor.GetField(
                "m_view",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance
            );
            if (fieldView == null)
                throw new ArgumentNullException();

            var fieldOffset = tView.GetField(
                "m_pointerOffset",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance
            );
            if (fieldOffset == null)
                throw new ArgumentNullException();

            var argument = Expression.Parameter(tAccessor, "accessor");
            var expr = Expression.Field(
                Expression.Field(argument, fieldView), fieldOffset
            );

            return Expression.Lambda<GetPointerOffsetFunc>(
                expr, "GetPointerOffset", new[] { argument }
            ).Compile();
        }

        internal static SafeBuffer GetSafeBuffer (this UnmanagedMemoryAccessor accessor) {
            var buffer = _GetSafeBuffer(accessor);
            if (buffer == null)
                throw new InvalidDataException();
            return buffer;
        }

        internal static Int64 GetPointerOffset (this MemoryMappedViewAccessor accessor) {
            return _GetPointerOffset(accessor);
        }

        internal static ArraySegment<byte> GetSegment (this MemoryStream stream) {
            if (stream.Length >= int.MaxValue)
                throw new InvalidDataException();

            return new ArraySegment<byte>(stream.GetBuffer(), 0, (int)stream.Length);
        }
    }

    public unsafe delegate void GenericPtrToStructureFunc<T> (byte* source, out T destination, uint size)
        where T : struct;
    public unsafe delegate void GenericStructureToPtrFunc<T> (ref T source, byte* destination, uint size)
        where T : struct;

    public unsafe static class Unsafe<T>
        where T : struct {

        public static readonly GenericPtrToStructureFunc<T> PtrToStructure;
        public static readonly GenericStructureToPtrFunc<T> StructureToPtr;

        static Unsafe () {
            var tSafeBuffer = typeof(SafeBuffer);

            PtrToStructure = (GenericPtrToStructureFunc<T>)Delegate.CreateDelegate(
                typeof(GenericPtrToStructureFunc<T>), 
                tSafeBuffer.GetMethod(
                    "GenericPtrToStructure", 
                    System.Reflection.BindingFlags.Static | 
                    System.Reflection.BindingFlags.NonPublic
                )
            );

            StructureToPtr = (GenericStructureToPtrFunc<T>)Delegate.CreateDelegate(
                typeof(GenericStructureToPtrFunc<T>),
                tSafeBuffer.GetMethod(
                    "GenericStructureToPtr",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic
                )
            );
        }
    }
}