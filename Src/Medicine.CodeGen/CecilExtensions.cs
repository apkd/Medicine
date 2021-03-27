using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.Collections.LowLevel.Unsafe;
using Unity.CompilationPipeline.Common.Diagnostics;
using static System.StringComparison;
using ICustomAttributeProvider = Mono.Cecil.ICustomAttributeProvider;

namespace Medicine
{
    static class CecilExtensions
    {
        static readonly ThreadLocal<ModuleDefinition> currentModule
            = new ThreadLocal<ModuleDefinition>();

        static readonly int sourceDirectoryTrimLength
            = Environment.CurrentDirectory.Length + 1;

        public static ModuleDefinition CurrentModule
        {
            get => currentModule.Value;
            set => currentModule.Value = value;
        }

        public static string GetFilenameLineColumnString(this MethodDebugInformation methodDebugInformation)
        {
            if (methodDebugInformation.SequencePoints.FirstOrDefault() is SequencePoint seq)
                return $" (at {seq.Document.Url.Substring(sourceDirectoryTrimLength).Replace('\\', '/')}:{seq.StartLine})";

            return "";
        }

        public static DiagnosticMessage GetDiagnosticMessage(this MethodDefinition method, string messageData, DiagnosticType diagnosticType = DiagnosticType.Warning)
        {
            var seq = method.DebugInformation?.SequencePoints?.FirstOrDefault();

            var file = seq?.Document.Url;
            var line = seq?.StartLine ?? -1;
            var column = seq?.StartColumn ?? -1;

            string GetMessage()
            {
                if (string.IsNullOrWhiteSpace(file))
                    return $"{messageData}\n\nMedicineILPostProcessor() (unknown location)";

                return line < 0
                    ? $"{messageData}\n\nMedicineILPostProcessor() (at {file})"
                    : $"{messageData}\n\nMedicineILPostProcessor() (at {file}:{line})";
            }

            return new DiagnosticMessage
            {
                File = file,
                Line = line,
                Column = column,
                DiagnosticType = diagnosticType,
                MessageData = GetMessage(),
            };
        }

        public static string GetName(this CustomAttribute attribute)
            => $"[{attribute.AttributeType.FullName.Replace('/', '.')}]";

        public static TypeReference Import(this TypeReference typeReference)
            => CurrentModule.ImportReference(typeReference);

        public static FieldReference Import(this FieldReference fieldReference)
            => CurrentModule.ImportReference(fieldReference);

        public static TypeReference Import(this Type type)
            => CurrentModule.ImportReference(type);

        public static MethodReference Import(this MethodReference methodReference)
            => CurrentModule.ImportReference(methodReference);

        public static MethodReference Import(this MethodBase methodInfo)
            => CurrentModule.ImportReference(methodInfo);

        public static FieldReference Import(this FieldInfo fieldInfo)
            => CurrentModule.ImportReference(fieldInfo);

        public static TypeDefinition ResolveFast(this TypeReference typeReference)
            => typeReference is TypeDefinition typeDefinition
                ? typeDefinition
                : typeReference.Resolve();

        public static FieldDefinition ResolveFast(this FieldReference fieldReference)
            => fieldReference is FieldDefinition fieldDefinition
                ? fieldDefinition
                : fieldReference.Resolve();

        public static bool HasAttribute<T>(this ICustomAttributeProvider type) where T : System.Attribute
        {
            if (type.CustomAttributes is var attributes)
                for (int i = 0, n = attributes.Count; i < n; i++)
                    if (attributes[i].Is<T>())
                        return true;

            return false;
        }

        public static bool IsCompilerGenerated(this ICustomAttributeProvider type)
            => type.HasAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>();

        public static bool BaseTypesInclude(this TypeReference type, Func<TypeReference, bool> filter)
        {
            try
            {
                while ((type = type.ResolveFast().BaseType) != null)
                    if (filter(type))
                        return true;
            }
            catch
            {
                /* silently ignore exceptions; we're probably unable to resolve the base type's assembly. assume false result */
            }

            return false;
        }

        public static bool DerivesFrom<T>(this TypeReference type)
            => type.BaseTypesInclude(x => x.Is<T>());

        public static bool Is<T>(this TypeReference @this)
        {
            var type = @this;
            var other = typeof(T);

            while (true)
            {
                if (!type.Name.Equals(other.Name, Ordinal))
                    return false;

                var declaringType = type.DeclaringType;
                var otherDeclaringType = other.DeclaringType;

                if ((declaringType == null) != (otherDeclaringType == null))
                    return false;

                if (declaringType == null)
                    return (type.Namespace ?? "").Equals(other.Namespace ?? "", Ordinal);

                type = declaringType;
                other = otherDeclaringType;
            }
        }

        public static bool Is<T>(this CustomAttribute attribute)
            => attribute.AttributeType.Is<T>();

        public static bool HasProperty<T>(this CustomAttribute attribute, string key, T value)
        {
            for (int i = 0, n = attribute.Properties.Count; i < n; ++i)
                if (attribute.Properties[i] is var property)
                    if (property.Name.Equals(key, Ordinal))
                        if (property.Argument.Value.Equals(value))
                            return true;

            return false;
        }

        public static bool HasFlagNonAlloc<T>(this T enumValue, T enumFlag) where T : struct, Enum
        {
            int a = UnsafeUtility.EnumToInt(enumValue);
            int b = UnsafeUtility.EnumToInt(enumFlag);
            return (a & b) == b;
        }

        public static TypeReference UnwrapArrayElementType(this TypeReference type)
            => type is ArrayType arrayType ? arrayType.ElementType : type;

        public static TypeReference UnwrapGenericInstanceType(this TypeReference type)
            => type is GenericInstanceType genericInstanceType ? genericInstanceType.ElementType : type;

        public static MethodReference MakeHostInstanceGeneric(this MethodReference self, params TypeReference[] args)
        {
            var reference = new MethodReference(self.Name, self.ReturnType, self.DeclaringType.UnwrapGenericInstanceType().MakeGenericInstanceType(args))
            {
                HasThis = self.HasThis,
                ExplicitThis = self.ExplicitThis,
                CallingConvention = self.CallingConvention,
            };
            var referenceParameters = reference.Parameters;
            var referenceGenericParameters = reference.GenericParameters;

            var parameters = self.Parameters;
            for (int i = 0, n = parameters.Count; i < n; i++)
                referenceParameters.Add(new ParameterDefinition(parameters[i].ParameterType));

            var genericParameters = self.GenericParameters;
            for (int i = 0, n = genericParameters.Count; i < n; i++)
                referenceGenericParameters.Add(new GenericParameter(genericParameters[i].Name, reference));

            return reference;
        }

        public static GenericInstanceMethod MakeGenericInstanceMethod(this MethodReference method, params TypeReference[] genericArguments)
        {
            if (method.GenericParameters.Count != genericArguments.Length)
                throw new ArgumentException($"method.GenericParameters.Count({method.GenericParameters.Count}) != genericArguments.Length({genericArguments.Length})");

            var methodGenericInstance = new GenericInstanceMethod(method);
            var instanceGenericArguments = methodGenericInstance.GenericArguments;

            for (int i = 0, n = genericArguments.Length; i < n; i++)
                instanceGenericArguments.Add(genericArguments[i]);

            return methodGenericInstance;
        }

        public static GenericInstanceType MakeGenericInstanceType(this TypeReference type, params TypeReference[] genericArguments)
        {
            if (type.GenericParameters.Count != genericArguments.Length)
                throw new ArgumentException($"type.GenericParameters.Count({type.GenericParameters.Count}) != genericArguments.Length({genericArguments.Length})");

            var typeGenericInstance = new GenericInstanceType(type);
            var instanceGenericArguments = typeGenericInstance.GenericArguments;

            for (int i = 0, n = genericArguments.Length; i < n; i++)
                instanceGenericArguments.Add(genericArguments[i]);

            return typeGenericInstance;
        }
    }
}
