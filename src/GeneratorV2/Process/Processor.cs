﻿using GeneratorV2.Data;
using GeneratorV2.Parsing;
using GeneratorV2.Writing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GeneratorV2.Process
{
    static class Processor
    {
        public static OutputData ProcessSpec(Specification spec)
        {
            // FIXME: Check if GLSC 2 contains unique functions that could be filtered out.
            // FIXME: See if we can filter out enums and functions that are from GLSC 2 so that
            // we do not process them that would be great.

            // The first thing we do is process all of the functions defined into a dictionary of NativeFunctions
            Dictionary<string, NativeFunction> allFunctions = new Dictionary<string, NativeFunction>(spec.Commands.Count);
            Dictionary<NativeFunction, Overload[]> allFunctionOverloads = new Dictionary<NativeFunction, Overload[]>(spec.Commands.Count);
            Dictionary<NativeFunction, string[]> functionToEnumGroupsUsed = new Dictionary<NativeFunction, string[]>();
            foreach (var command in spec.Commands)
            {
                var nativeFunction = MakeNativeFunction(command, out string[] usedEnumGroups);
                allFunctions.Add(nativeFunction.EntryPoint, nativeFunction);

                functionToEnumGroupsUsed.Add(nativeFunction, usedEnumGroups);

                var overloads = GenerateOverloads(nativeFunction);
                allFunctionOverloads[nativeFunction] = overloads;
            }

            // FIXME: Make this into Dictionary<GLAPI, Dictionary<string, EnumMemberData>> to handle different GL APIs.
            // We then make a dictionary of all the enums with their individual group data inside.
            Dictionary<string, EnumMemberData> allEnums = new Dictionary<string, EnumMemberData>();
            Dictionary<string, EnumMemberData> allEnumsGLES = new Dictionary<string, EnumMemberData>();
            // FIXME: This is only here to mark groups as flags...
            // FIXME: Rename/change type to Dictionary<string, bool> isEnumGroupFlags.
            Dictionary<string, EnumGroupData> allGroups = new Dictionary<string, EnumGroupData>();
            // FIXME: Remove this and use the variable above (isEnumGroupFlags.Keys) instead.
            HashSet<string> allGroupNames = new HashSet<string>();
            foreach (var enumsEntry in spec.Enums)
            {
                // FIXME: Cleanup.
                // FIXME: Fix specification so we don't need to apply the <enums> groups to the enums here.
                bool isFlag = enumsEntry.Type == EnumType.Bitmask;
                HashSet<string> entryGroups = new HashSet<string>();
                if (enumsEntry.Groups != null)
                {
                    allGroupNames.UnionWith(enumsEntry.Groups);
                    entryGroups.UnionWith(enumsEntry.Groups);
                    foreach (var group in enumsEntry.Groups)
                    {
                        allGroups.TryAdd(group, new EnumGroupData(group, isFlag));
                    }
                }

                foreach (var @enum in enumsEntry.Enums)
                {
                    HashSet<string> groups = new HashSet<string>(entryGroups);

                    if (@enum.Groups != null)
                    {
                        allGroupNames.UnionWith(@enum.Groups);
                        groups.UnionWith(@enum.Groups);
                        foreach (var group in @enum.Groups)
                        {
                            allGroups.TryAdd(group, new EnumGroupData(group, isFlag));
                        }
                    }

                    var data = new EnumMemberData(NameMangler.MangleEnumName(@enum.Name), @enum.Value, groups.ToArray(), isFlag);
                    if (@enum.Api == GLAPI.None)
                    {
                        allEnums.Add(@enum.Name, data);
                        allEnumsGLES.Add(@enum.Name, data);
                    }
                    else if (@enum.Api == GLAPI.GLES2 || @enum.Api == GLAPI.GLES1)
                    {
                        allEnumsGLES.Add(@enum.Name, data);
                    }
                    else if (@enum.Api == GLAPI.GL)
                    {
                        allEnums.Add(@enum.Name, data);
                    }
                }
            }

            // Now that we have all of the functions and enums ready in dictionaries
            // we can start building all of the API versions.

            // Filter the features we actually want to emit
            List<Feature> features = new List<Feature>();
            foreach (var feature in spec.Features)
            {
                switch (feature.Api)
                {
                    case GLAPI.GL:
                    case GLAPI.GLES1:
                    case GLAPI.GLES2:
                        features.Add(feature);
                        break;
                    case GLAPI.GLSC2:
                        // We don't care about GLSC 2
                        continue;
                    case GLAPI.Invalid:
                    case GLAPI.None:
                    default:
                        throw new Exception($"Feature '{feature.Name}' doesn't have a proper api tag.");
                }
            }

            List<GLVersionOutput> glVersions = new List<GLVersionOutput>();

            // Here we process all of the desktop OpenGL versions
            {
                HashSet<NativeFunction> functionsInLastVersion = new HashSet<NativeFunction>();
                HashSet<EnumMemberData> enumsInLastVersion = new HashSet<EnumMemberData>();
                HashSet<string> groupsReferencedByFunctions = new HashSet<string>();
                foreach (var feature in features)
                {
                    bool isGLES = feature.Api switch
                    {
                        GLAPI.GL => false,
                        GLAPI.GLES1 => true,
                        GLAPI.GLES2 => true,
                        _ => throw new Exception($"We should filter other APIs before this. API: {feature.Api}"),
                    };

                    StringBuilder name = new StringBuilder();
                    name.Append(feature.Api switch {
                        GLAPI.GLES1 => "GLES",
                        GLAPI.GLES2 => "GLES",
                        _ => feature.Api,
                    });
                    name.Append(feature.Version.Major);
                    name.Append(feature.Version.Minor);

                    // A list of functions contained in this version.
                    HashSet<NativeFunction> functions = new HashSet<NativeFunction>();
                    HashSet<EnumMemberData> enums = new HashSet<EnumMemberData>();

                    // Go through all the functions that are required for this version and add them here.
                    foreach (var require in feature.Requires)
                    {
                        foreach (var command in require.Commands)
                        {
                            if (allFunctions.TryGetValue(command, out var function))
                            {
                                functions.Add(function);

                                var functionGroups = functionToEnumGroupsUsed[function];
                                groupsReferencedByFunctions.UnionWith(functionGroups);
                            }
                            else
                            {
                                throw new Exception($"Could not find any function called '{command}'.");
                            }
                        }

                        foreach (var enumName in require.Enums)
                        {
                            var enumsDict = isGLES ? allEnumsGLES : allEnums;
                            if (enumsDict.TryGetValue(enumName, out var @enum))
                            {
                                enums.Add(@enum);
                            }
                            else
                            {
                                throw new Exception($"Could not find any enum called '{enumName}'.");
                            }
                        }
                    }

                    // Make a copy of all the functions and enums contained in the previous version.
                    // We will remove items from this list according to the remove tags.
                    HashSet<NativeFunction> functionsFromPreviousVersion = new HashSet<NativeFunction>(functionsInLastVersion);
                    HashSet<EnumMemberData> enumsFromPreviousVersion = new HashSet<EnumMemberData>(enumsInLastVersion);

                    foreach (var remove in feature.Removes)
                    {
                        // FXIME: For now we don't remove anything as idk how we want to
                        // handle core vs not core. For now we just include all versions.

                        // We probably want to mark deprecated functions with a deprecated tag?
                    }

                    // Add all of the functions from the last version.
                    functions.UnionWith(functionsFromPreviousVersion);
                    enums.UnionWith(enumsFromPreviousVersion);
                    //enums.AddRange();

                    // This is the new previous version.
                    functionsInLastVersion = functions;
                    enumsInLastVersion = enums;

                    // Go through all of the enums and put them into their groups
                    Dictionary<string, List<EnumMemberData>> enumGroups = new Dictionary<string, List<EnumMemberData>>();

                    // Add keys + lists for all enum names
                    foreach (var groupName in allGroupNames)
                    {
                        enumGroups.Add(groupName, new List<EnumMemberData>());
                    }

                    foreach (var @enum in enums)
                    {
                        // This enum doesn't have a group, so we skip it.
                        // It will still appear in the All enum.
                        if (@enum.Groups == null) continue;

                        foreach (var groupName in @enum.Groups)
                        {
                            // Here we rely on the step where we add all of the enum groups earlier.
                            enumGroups[groupName].Add(@enum);
                        }
                    }

                    List<EnumGroup> finalGroups = new List<EnumGroup>();
                    foreach (var (groupName, members) in enumGroups)
                    {
                        // SpecialNumbers is not an enum group that we want to output.
                        // We handle these entries differently as some of the entries are longer than an int.
                        if (groupName == "SpecialNumbers")
                            continue;

                        // Remove all empty enum groups, except the empty groups referenced by included functions.
                        // In GL 4.1 to 4.5 there are functions that use the group "ShaderBinaryFormat"
                        // while not including any members for that enum group.
                        // This is needed to solve that case.
                        if (members.Count <= 0 && groupsReferencedByFunctions.Contains(groupName) == false)
                            continue;

                        bool isFlags = allGroups[groupName].IsFlags;
                        finalGroups.Add(new EnumGroup(groupName, isFlags, members));
                    }

                    var functionList = functions.ToList();
                    List<Overload> overloadList = new List<Overload>();
                    foreach (var function in functionList)
                    {
                        overloadList.AddRange(allFunctionOverloads[function]);
                    }

                    glVersions.Add(new GLVersionOutput(name.ToString(), functionList, overloadList, enums.ToList(), finalGroups));
                }
            }

            return new OutputData(glVersions);
        }

        public static NativeFunction MakeNativeFunction(Command2 command, out string[] enumGroupsUsed)
        {
            HashSet<string> enumGroups = new HashSet<string>();

            List<Parameter> parameters = new List<Parameter>();
            foreach (var p in command.Parameters)
            {
                ICSType t = MakeCSType(p.Type.Type, p.Type.Group);
                parameters.Add(new Parameter(t, NameMangler.MangleParameterName(p.Name), p.Length));
                if (p.Type.Group != null)
                    enumGroups.Add(p.Type.Group);
            }

            ICSType returnType = MakeCSType(command.ReturnType.Type, command.ReturnType.Group);
            if (command.ReturnType.Group != null)
                enumGroups.Add(command.ReturnType.Group);

            enumGroupsUsed = enumGroups.ToArray();

            return new NativeFunction(command.EntryPoint, parameters, returnType);
        }

        public static ICSType MakeCSType(GLType type, string? group = null)
        {
            switch (type)
            {
                case GLArrayType at:
                    return new CSFixedSizeArray(MakeCSType(at.BaseType, group), at.Length, at.Const);
                case GLPointerType pt:
                    return new CSPointer(MakeCSType(pt.BaseType, group), pt.Const);
                case GLBaseType bt:
                    return bt.Type switch
                    {
                        PrimitiveType.Void => new CSVoid(),
                        PrimitiveType.Byte => new CSType("byte", bt.Const),
                        PrimitiveType.Char8 => new CSChar(isByteSize: true, bt.Const),
                        PrimitiveType.Sbyte => new CSType("sbyte", bt.Const),
                        PrimitiveType.Short => new CSType("short", bt.Const),
                        PrimitiveType.Ushort => new CSType("ushort", bt.Const),
                        PrimitiveType.Int => new CSType("int", bt.Const),
                        PrimitiveType.Uint => new CSType("uint", bt.Const),
                        PrimitiveType.Long => new CSType("long", bt.Const),
                        PrimitiveType.Ulong => new CSType("ulong", bt.Const),
                        // This might need an include, but the spec doesn't use this type
                        // so we don't really need to do anything...
                        PrimitiveType.Half => new CSType("Half", bt.Const),
                        PrimitiveType.Float => new CSType("float", bt.Const),
                        PrimitiveType.Double => new CSType("double", bt.Const),
                        PrimitiveType.IntPtr => new CSType("IntPtr", bt.Const),

                        PrimitiveType.VoidPtr => new CSPointer(new CSVoid(), bt.Const),

                        // FIXME: Should this be treated special?
                        PrimitiveType.Enum => new CSType(group ?? "All", bt.Const),

                        // FIXME: Are these just normal CSType? probably...
                        PrimitiveType.GLHandleARB => new CSType("GLHandleARB", bt.Const),
                        PrimitiveType.GLSync => new CSType("GLSync", bt.Const),

                        PrimitiveType.CLContext => new CSType("CLContext", bt.Const),
                        PrimitiveType.CLEvent => new CSType("CLEvent", bt.Const),

                        PrimitiveType.GLDEBUGPROC => new CSType("GLDebugProc", bt.Const),
                        PrimitiveType.GLDEBUGPROCARB => new CSType("GLDebugProcARB", bt.Const),
                        PrimitiveType.GLDEBUGPROCKHR => new CSType("GLDebugProcKHR", bt.Const),
                        PrimitiveType.GLDEBUGPROCAMD => new CSType("GLDebugProcAMD", bt.Const),
                        PrimitiveType.GLDEBUGPROCNV => new CSType("GLDebugProcNV", bt.Const),

                        PrimitiveType.Invalid => throw new Exception(),
                        _ => throw new Exception(),
                    };
                default:
                    throw new Exception();
            }
        }

        static readonly IOverloader[] Overloaders = new IOverloader[]
        {
            new StringReturnOverloader(),
            new StringOverloader(),
            new SpanAndArrayOverloader(),
            new RefInsteadOfPointerOverloader(),
        };

        // FIXME: The return variable my go out of scope, declare the variables the first thing we do.
        // FIXME: Figure out how to cast ref/out/in to pointers.
        // FIXME: Figure out how we do return type overloading? Do we rename the raw function to something else?
        // FIXME: Should we only be able to have one return type overload?
        // Maybe we can do the return type overloading in a post processing step?
        public static Overload[] GenerateOverloads(NativeFunction function)
        {
            List<Overload> overloads = new List<Overload>
            {
                // Make a "base" overload
                new Overload(null, null, function.Parameters.ToArray(), function, function.ReturnType, Array.Empty<string>()),
            };

            bool overloadedOnce = false;
            foreach (var overloader in Overloaders)
            {
                List<Overload> newOverloads = new List<Overload>();
                foreach (var overload in overloads)
                {
                    if (overloader.TryGenerateOverloads(overload, out var overloaderOverloads))
                    {
                        overloadedOnce = true;

                        newOverloads.AddRange(overloaderOverloads);
                    }
                    else
                    {
                        newOverloads.Add(overload);
                    }
                }
                // Replace the old overloads with the new overloads
                overloads = newOverloads;
            }

            if (overloadedOnce)
            {
                return overloads.ToArray();
            }
            else
            {
                return Array.Empty<Overload>();
            }
        }

        public delegate Overload[] Overloader(Overload overload);

        // FIXME: Make this nicer.
        // These being classes actually don't acomplish much
        // they are just so that we can put them in a
        // IOverloader[].
        class StringReturnOverloader : IOverloader
        {
            public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
            {
                // See: https://github.com/KhronosGroup/OpenGL-Registry/issues/363
                // These are the only two functions that return strings 2020-12-29
                if (overload.NativeFunction.EntryPoint == "glGetString" ||
                    overload.NativeFunction.EntryPoint == "glGetStringi")
                {
                    var layer = new StringReturnLayer();
                    var returnType = new CSString(nullable: true);
                    newOverloads = new List<Overload>()
                    {
                        new Overload(overload, layer, overload.InputParameters, overload.NativeFunction, returnType, overload.GenericTypes)
                    };
                    return true;
                }
                else
                {
                    newOverloads = default;
                    return false;
                }
            }

            class StringReturnLayer : IOverloadLayer
            {
                public void WritePrologue(IndentedTextWriter writer)
                {
                }

                public string? WriteEpilogue(IndentedTextWriter writer, string? returnName)
                {
                    string returnNameNew = $"{returnName}_str";
                    writer.WriteLine($"string? {returnNameNew} = Marshal.PtrToStringAnsi((IntPtr){returnName});");
                    return returnNameNew;
                }
            }
        }

        class StringOverloader : IOverloader
        {
            public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
            {
                List<Parameter> newParams = new List<Parameter>(overload.InputParameters);
                for (int i = newParams.Count - 1; i >= 0; i--)
                {
                    // FIXME: We want to handle sized strings different!!!
                    var param = newParams[i];
                    if (param.Type is CSPointer pt && pt.BaseType is CSChar charType)
                    {
                        var pointerParam = newParams[i];
                        // FIXME: Can we know if the string is nullable or not?
                        newParams[i] = new Parameter(new CSString(nullable: false), param.Name + "_string", null);
                        var stringParams = newParams.ToArray();
                        var stringLayer = new StringLayer(pointerParam, newParams[i]);

                        // FIXME: There might be more than one parameter we should do this for...
                        newOverloads = new List<Overload>()
                        {
                            new Overload(overload, stringLayer, stringParams, overload.NativeFunction, overload.ReturnType, overload.GenericTypes),
                        };
                        return true;
                    }
                }

                newOverloads = default;
                return false;
            }

            class StringLayer : IOverloadLayer
            {
                public readonly Parameter PointerParameter;
                public readonly Parameter StringParameter;

                public StringLayer(Parameter pointerParameter, Parameter stringParameter)
                {
                    PointerParameter = pointerParameter;
                    StringParameter = stringParameter;
                }

                public void WritePrologue(IndentedTextWriter writer)
                {
                    writer.WriteLine($"byte* {PointerParameter.Name} = (byte*)Marshal.StringToCoTaskMemUTF8({StringParameter.Name});");
                }

                public string? WriteEpilogue(IndentedTextWriter writer, string? returnName)
                {
                    writer.WriteLine($"Marshal.FreeCoTaskMem((IntPtr){PointerParameter.Name});");
                    return returnName;
                }
            }
        }

        class SpanAndArrayOverloader : IOverloader
        {
            public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
            {
                if (overload.NativeFunction.EntryPoint == "glShaderSource")
                {
                    ;
                }
                if (overload.NativeFunction.EntryPoint == "glTransformFeedbackVaryings"||
                    overload.NativeFunction.EntryPoint == "glCreateShaderProgramv")
                {
                    Debug.Print($"Fix proper overloads for: {overload.NativeFunction.EntryPoint}");
                    ;

                    newOverloads = default;
                    return false;
                }

                // FIXME: We want to be able to handle more than just one Span and Array overload
                // functions like "glShaderSource" can take more than one array.
                // 
                List<Parameter> newParams = new List<Parameter>(overload.InputParameters);
                for (int i = newParams.Count - 1; i >= 0; i--)
                {
                    var param = newParams[i];

                    if (param.Type is CSPointer pt && pt.BaseType is CSChar)
                    {
                        Debug.WriteLine($"Char pointer leaked from earlier overloaders: \"{overload.NativeFunction.EntryPoint}\" ({param})");
                        break;
                    }

                    if (param.Length != null)
                    {
                        string? paramName = GetParameterExpression(param.Length, out var expr);
                        if (paramName != null)
                        {
                            // FIXME: This can be replaced with `i`?
                            int index = newParams.FindIndex(p => p.Name == paramName);

                            var pointerParam = newParams[i];
                            // FIXME: Check this!
                            var pointer = pointerParam.Type as CSPointer;

                            string[]? genericTypes = overload.GenericTypes;
                            ICSType baseType;
                            if (pointer.BaseType is CSVoid)
                            {
                                genericTypes = overload.GenericTypes.MakeCopyAndGrow(1);
                                genericTypes[^1] = $"T{genericTypes.Length}";
                                baseType = new CSGenericType(genericTypes[^1]);
                            }
                            else
                            {
                                baseType = pointer.BaseType;
                            }

                            var old = newParams[index];

                            newParams.RemoveAt(index);
                            int typeIndex = index < i ? i - 1 : i;

                            // FIXME: Name of new parameter
                            newParams[typeIndex] = new Parameter(new CSSpan(baseType, pointer.Constant), pointerParam.Name + "_span", null);
                            var spanParams = newParams.ToArray();
                            var spanLayer = new SpanOrArrayLayer(pointerParam, newParams[typeIndex], old, expr(newParams[typeIndex].Name));

                            newParams[typeIndex] = new Parameter(new CSArray(baseType, pointer.Constant), pointerParam.Name + "_array", null);
                            var arrayParams = newParams.ToArray();
                            var arrayLayer = new SpanOrArrayLayer(pointerParam, newParams[typeIndex], old, expr(newParams[typeIndex].Name));

                            // FIXME: There might be more than one parameter we should do this for...
                            newOverloads = new List<Overload>()
                            {
                                new Overload(overload, spanLayer, spanParams, overload.NativeFunction, overload.ReturnType, genericTypes),
                                new Overload(overload, arrayLayer, arrayParams, overload.NativeFunction, overload.ReturnType, genericTypes),
                            };
                            return true;
                        }
                    }
                }

                newOverloads = default;
                return false;

                // FIXME: Better name, maybe even another structure...
                static string? GetParameterExpression(IExpression expr, out Func<string, string> parameterExpression)
                {
                    switch (expr)
                    {
                        case Constant c:
                            parameterExpression = s => c.Value.ToString();
                            return null;
                        case ParameterReference pr:
                            parameterExpression = s => $"{s}.Length";
                            return pr.ParameterName;
                        case BinaryOperation bo:
                            // FIXME: We don't want to assume that the left expression contains the
                            // parameter name, but this is true for gl.xml 2020-12-30
                            string? reference = GetParameterExpression(bo.Left, out var leftExpr);
                            GetParameterExpression(bo.Right, out var rightExpr);
                            var invOp = BinaryOperation.Invert(bo.Operator);
                            parameterExpression = s => $"{leftExpr(s)} {BinaryOperation.GetOperationChar(invOp)} {rightExpr(s)}";
                            return reference;
                        default:
                            parameterExpression = s => "";
                            return null;
                    }
                }
            }

            class SpanOrArrayLayer : IOverloadLayer
            {
                public readonly Parameter PointerParameter;
                public readonly Parameter SpanOrArrayParameter;
                public readonly Parameter LengthParameter;
                public readonly string ParameterExpression;

                public SpanOrArrayLayer(Parameter pointerParameter, Parameter spanOrArrayParameter, Parameter lengthParameter, string parameterExpression)
                {
                    PointerParameter = pointerParameter;
                    SpanOrArrayParameter = spanOrArrayParameter;
                    LengthParameter = lengthParameter;
                    ParameterExpression = parameterExpression;
                }

                private IndentedTextWriter.Scope Scope;
                public void WritePrologue(IndentedTextWriter writer)
                {
                    // NOTE: We are casting the length field to the target type because some of
                    // the functions don't take `int` types directly, instead they take an `IntPtr`.
                    // But that is fine because we can cast `int`s to `IntPtr`.
                    // This is slightly fragile but it's fine for now.
                    // - Noggin_bops 2021-01-22
                    writer.WriteLine($"{LengthParameter.Type.ToCSString()} {LengthParameter.Name} = ({LengthParameter.Type.ToCSString()}){ParameterExpression};");
                    writer.WriteLine($"fixed ({PointerParameter.Type.ToCSString()} {PointerParameter.Name} = {SpanOrArrayParameter.Name})");
                    writer.WriteLine("{");
                    Scope = writer.Indentation();
                }

                public string? WriteEpilogue(IndentedTextWriter writer, string? returnName)
                {
                    Scope.Dispose();
                    writer.WriteLine("}");
                    return returnName;
                }
            }
        }

        class RefInsteadOfPointerOverloader : IOverloader
        {
            public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
            {
                Parameter[] parameters = new Parameter[overload.InputParameters.Length];
                List<Parameter> original = new List<Parameter>();
                List<Parameter> changed = new List<Parameter>();
                for (int i = 0; i < overload.InputParameters.Length; i++)
                {
                    Parameter parameter = overload.InputParameters[i];
                    parameters[i] = parameter;

                    if (parameter.Type is CSPointer pt && pt.BaseType is CSType)
                    {
                        // FIXME: When do we know it's an out ref type?
                        CSRef.Type refType = CSRef.Type.Ref;
                        string postfix = "_ref";
                        if (pt.Constant)
                        {
                            refType = CSRef.Type.In;
                            postfix = "_in";
                        }

                        original.Add(parameters[i]);

                        parameters[i] = new Parameter(new CSRef(refType, pt.BaseType), parameter.Name + postfix, parameter.Length);

                        changed.Add(parameters[i]);
                    }
                }

                if (changed.Count > 0)
                {
                    var layer = new RefInsteadOfPointerLayer(changed, original);
                    newOverloads = new List<Overload>()
                    {
                        new Overload(overload, layer, parameters, overload.NativeFunction, overload.ReturnType, overload.GenericTypes)
                    };
                    return true;
                }
                else
                {
                    newOverloads = default;
                    return false;
                }
            }

            class RefInsteadOfPointerLayer : IOverloadLayer
            {
                public List<Parameter> RefParameters;
                public List<Parameter> PointerParameters;

                public RefInsteadOfPointerLayer(List<Parameter> refParameters, List<Parameter> pointerParameters)
                {
                    RefParameters = refParameters;
                    PointerParameters = pointerParameters;
                }

                private IndentedTextWriter.Scope Scope;
                public void WritePrologue(IndentedTextWriter writer)
                {
                    for (int i = 0; i < RefParameters.Count; i++)
                    {
                        string type = PointerParameters[i].Type.ToCSString();
                        writer.WriteLine($"fixed ({type} {PointerParameters[i].Name} = &{RefParameters[i].Name})");
                    }

                    writer.WriteLine("{");
                    Scope = writer.Indentation();
                }

                public string? WriteEpilogue(IndentedTextWriter writer, string? returnName)
                {
                    Scope.Dispose();
                    writer.WriteLine("}");
                    return returnName;
                }
            }
        }
    }
}
