//
// The Open Toolkit Library License
//
// Copyright (c) 2006 - 2015 Stefanos Apostolopoulos for the Open Toolkit Library
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do
// so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Text;
using System.Runtime.InteropServices;

namespace OpenTK.Graphics.OpenGL4
{
    public sealed partial class GL
    {
        /// <summary>
        /// [requires: v4.3 or ARB_shader_storage_buffer_object|VERSION_4_3]
        /// Change an active shader storage block binding
        /// </summary>
        /// <param name="program">
        /// The name of the program containing the block whose binding to change.
        /// </param>
        /// <param name="storageBlockIndex">
        /// The index storage block within the program.
        /// </param>
        /// <param name="storageBlockBinding">
        /// The index storage block binding to associate with the specified storage block.
        /// </param>
        [AutoGenerated(Category = "ARB_shader_storage_buffer_object|VERSION_4_3", Version = "4.3", EntryPoint = "glShaderStorageBlockBinding")]
        public static void ShaderStorageBlockBinding(uint program, uint storageBlockIndex, uint storageBlockBinding)
        {
            throw new BindingsNotRewrittenException();
        }

        [Slot(1016)]
        [DllImport(Library, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void glShaderStorageBlockBinding(uint program, uint storageBlockIndex, uint storageBlockBinding);
    }
}