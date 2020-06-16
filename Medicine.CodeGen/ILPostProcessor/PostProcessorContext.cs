using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Medicine
{
    sealed class PostProcessorContext
    {
        public readonly ModuleDefinition Module;
        public readonly TypeDefinition[] Types;
        public readonly List<DiagnosticMessage> DiagnosticMessages;

        public PostProcessorContext(ModuleDefinition module)
        {
            Module = module;
            Types = GetAllTypeDefinitions(module).ToArray();
            DiagnosticMessages = new List<DiagnosticMessage>(capacity: 8);
        }

        static List<TypeDefinition> GetAllTypeDefinitions(ModuleDefinition moduleDefinition)
        {
            var result = new List<TypeDefinition>(capacity: moduleDefinition.Types.Count * 2);

            void AppendTypesRecursive(TypeDefinition parent)
            {
                result.Add(parent);
                var nested = parent.NestedTypes;
                for (int i = 0, n = nested.Count; i < n; i++)
                    AppendTypesRecursive(nested[i]);
            }

            var topLevelTypes = moduleDefinition.Types;

            for (int i = 0, n = topLevelTypes.Count; i < n; i++)
                AppendTypesRecursive(topLevelTypes[i]);

            return result;
        }
    }
}
