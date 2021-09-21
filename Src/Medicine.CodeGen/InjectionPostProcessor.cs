using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Unity.CompilationPipeline.Common.Diagnostics;
using UnityEngine;
using static System.StringComparison;
using static Mono.Cecil.Cil.Instruction;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Medicine
{
    /// <summary> Exceptions that indicate compiler warnings (recoverable errors). </summary>
    sealed class MedicineWarning : Exception
    {
        public MethodDefinition Site { get; }

        public MedicineWarning(string message, PropertyDefinition property) : base(message)
            => Site = property.GetMethod
                      ?? property.SetMethod
                      ?? property.DeclaringType.Properties.FirstOrDefault(x => x.GetMethod != null)?.GetMethod
                      ?? property.DeclaringType.Properties.FirstOrDefault(x => x.SetMethod != null)?.SetMethod
                      ?? property.DeclaringType.Methods.FirstOrDefault(x => x.DebugInformation != null);
    }

    /// <summary> Exceptions that indicate compiler errors (unrecoverable errors, eg. ones that occur mid-weaving). </summary>
    sealed class MedicineError : Exception
    {
        public MethodDefinition Site { get; }

        public MedicineError(string message, TypeDefinition type) : base(message)
            => Site = type.Properties.FirstOrDefault(x => x.GetMethod != null)?.GetMethod
                      ?? type.Properties.FirstOrDefault(x => x.SetMethod != null)?.SetMethod
                      ?? type.Methods.FirstOrDefault(x => x.DebugInformation != null);
    }

    sealed class InjectionPostProcessor
    {
        readonly PostProcessorContext context;

        public InjectionPostProcessor(PostProcessorContext context)
            => this.context = context;

        public void ProcessAssembly()
        {
            var properties = new List<(TypeDefinition, FieldDefinition, PropertyDefinition, CustomAttribute)>(capacity: 256);

            foreach (var type in context.Types)
            {
                var typeProperties = type.Properties;

                for (int i = 0, n = type.Properties.Count; i < n; i++)
                {
                    var property = typeProperties[i];

                    CustomAttribute GetMedicineInjectAttribute()
                    {
                        for (int j = 0, m = property.CustomAttributes.Count; j < m; j++)
                            if (property.CustomAttributes[j] is var attr)
                                if (attr.AttributeType.FullName.StartsWith($"{nameof(Medicine)}.{nameof(Inject)}", Ordinal))
                                    return attr;

                        return null;
                    }

                    var attribute = GetMedicineInjectAttribute();
                    if (attribute == null)
                        continue;

                    FieldDefinition GetPropertyBackingField()
                    {
                        // for an auto-implemented property getter, ldfld/ldsfld will be the first/second instruction
                        int m = Math.Min(property.GetMethod.Body.Instructions.Count, 2);

                        // look for ldfld/ldsfld instruction and return operand
                        for (int j = 0; j < m; j++)
                            if (property.GetMethod.Body.Instructions[j] is var instr)
                                if (instr.OpCode == OpCodes.Ldfld || instr.OpCode == OpCodes.Ldsfld)
                                    return instr.Operand as FieldDefinition;

                        return null;
                    }

                    if (property.GetMethod == null)
                    {
                        context.DiagnosticMessages.Add(
                            property.GetMethod.GetDiagnosticMessage(
                                $"Property <b>{property.PropertyType.Name} <i>{property.Name}</i></b> needs to have an auto-implemented getter in order to use injection."));
                        continue;
                    }

                    var backingField = GetPropertyBackingField();

                    if (backingField == null || !backingField.IsCompilerGenerated() || backingField.FieldType.FullName != property.PropertyType.FullName || backingField.IsPublic)
                    {
                        context.DiagnosticMessages.Add(
                            property.GetMethod.GetDiagnosticMessage(
                                $"Property <b>{property.PropertyType.Name} <i>{property.Name}</i></b> needs to be an auto-implemented property to use injection. For example:\n" +
                                $"<i>{attribute.GetName()}\n{property.PropertyType.Name} {property.Name} {{ get; }}</i>\n"));

                        continue;
                    }

                    if (backingField.HasAttribute<SerializeField>())
                    {
                        context.DiagnosticMessages.Add(
                            property.GetMethod.GetDiagnosticMessage(
                                $"Field <i>{backingField.FullName} cannot be serialized"));

                        continue;
                    }

                    properties.Add((type, backingField, property, attribute));
                }
            }

            foreach (var (type, field, property, attribute) in properties)
            {
                try
                {
                    TypeDefinition propertyType;

                    try
                    {
                        // try to resolve PropertyType to abort early if something's wrong
                        propertyType = property.PropertyType.ResolveFast() ?? throw new NullReferenceException();
                    }
                    catch
                    {
                        throw new MedicineWarning($"Unknown property type: {property.PropertyType.FullName}", property);
                    }

                    var isInterface = propertyType.IsInterface;
                    var isCamera = !isInterface && propertyType.Is<Camera>();
                    var isMonoBehaviour = !isCamera && propertyType.DerivesFrom<MonoBehaviour>();
                    var isComponent = !isCamera && (isMonoBehaviour || propertyType.DerivesFrom<Component>());
                    var isScriptableObject = !isCamera && propertyType.DerivesFrom<ScriptableObject>();
                    var isArray = property.PropertyType.IsArray;

                    if (attribute.Is<Inject.Single>())
                    {
                        if (!isInterface && !isCamera && !isMonoBehaviour && !isScriptableObject)
                            throw new MedicineWarning($"Type of property with [Inject.Single] needs to be a MonoBehaviour, a ScriptableObject, UnityEngine.Camera or an interface.", property);

                        if (isArray)
                            throw new MedicineWarning($"Type of property with [Inject.Single] must not be an array.", property);

                        if (property.SetMethod != null)
                            throw new MedicineWarning($"Property with [Inject.Single] must not have a setter.", property);

                        if (isCamera)
                        {
                            // special handling of Camera.main injection
                            ReplacePropertyGetterWithHelperMethod(type, property, MethodInfos.RuntimeHelpers.GetMainCamera);
                        }
                        else
                        {
                            if (!propertyType.HasAttribute<Register.Single>())
                                throw new MedicineWarning($"Type <i><b>{propertyType.FullName}</b></i> needs to be decorated with the [Register.Single] attribute in order to support singleton injection.", property);

                            // resolve object registered using [Register.Single]
                            ReplacePropertyGetterWithHelperMethod(type, property, MethodInfos.RuntimeHelpers.Singleton.GetInstance);
                        }

                        continue;
                    }

                    if (attribute.Is<Inject.All>())
                    {
                        if (!isInterface && !isMonoBehaviour && !isScriptableObject)
                            throw new MedicineWarning($"Type of property with [Inject.Single] needs to be an array of MonoBehaviours, ScriptableObjects, or interfaces.", property);

                        if (!isArray)
                            throw new MedicineWarning($"Type of property with {attribute.GetName()} needs to be an array.", property);

                        // todo: do we need this check?
                        // if (propertyType.UnwrapArrayElementType().ResolveFast() == null)
                        //     throw new CompilationWarningException($"Unknown {attribute.GetName()} array element type: {propertyType.FullName}", property);

                        if (!propertyType.HasAttribute<Register.All>())
                            throw new MedicineWarning($"Type <i><b>{propertyType.FullName}</b></i> needs to be decorated with the [Register.All] attribute in order to support collection injection.", property);

                        // resolve objects registered using [Register.All]
                        ReplacePropertyGetterWithHelperMethod(type, property, MethodInfos.RuntimeHelpers.Collection.GetInstance);
                        continue;
                    }

                    if (!isInterface && !isComponent)
                        throw new MedicineWarning($"Type of property with {attribute.GetName()} needs to be a component or an interface.", property);

                    if (type.Attributes.HasFlagNonAlloc(TypeAttributes.Abstract | TypeAttributes.Sealed))
                        throw new MedicineWarning($"Cannot use {attribute.GetName()} in a static class.", property);

                    // get this here to make sure we generate exceptions early
                    var initializationMethodInfo = GetInitializationMethodInfo(property, attribute);

                    // lazy injection
                    if (attribute.AttributeType.Name == nameof(Inject.Lazy))
                    {
                        ReplacePropertyGetterWithHelperMethod(
                            type, property,
                            helperMethod: initializationMethodInfo,
                            requireGameObjectArg: true
                        );
                        continue;
                    }

                    // if none of the above matched, it means we're using component initialization in Awake()
                    {
                        var awakeMethod = GetOrEmitMethodWithBaseCall(type, "Awake");
                        var initializationMethod = GetOrEmitInitializationMethod(type, "<Medicine>Initialize", callSite: awakeMethod);

                        InsertInitializationCall(
                            initializationMethod,
                            field,
                            property,
                            attribute,
                            initializationMethodInfo
                        );
                    }
                }
                catch (MedicineWarning ex)
                {
                    context.DiagnosticMessages.Add(ex.Site.GetDiagnosticMessage(ex.Message));
                }
                catch (Exception ex)
                {
                    context.DiagnosticMessages.Add(property.GetMethod.GetDiagnosticMessage(ex.ToString(), DiagnosticType.Error));
                    return;
                }
            }

            void EmitSingletonTypeRegistration(TypeReference registeredAs, TypeDefinition implementedBy)
            {
                InsertRegisteredInstanceInitializationCall(
                    method: GetOrEmitMethodWithBaseCall(implementedBy, "OnEnable"),
                    type: registeredAs,
                    helperMethod: MethodInfos.RuntimeHelpers.Singleton.RegisterInstance
                );
                InsertRegisteredInstanceInitializationCall(
                    method: GetOrEmitMethodWithBaseCall(implementedBy, "OnDisable"),
                    type: registeredAs,
                    helperMethod: MethodInfos.RuntimeHelpers.Singleton.UnregisterInstance
                );
            }

            void EmitCollectionTypeRegistration(TypeReference registeredAs, TypeDefinition implementedBy)
            {
                InsertRegisteredInstanceInitializationCall(
                    method: GetOrEmitMethodWithBaseCall(implementedBy, "OnEnable"),
                    type: registeredAs,
                    helperMethod: MethodInfos.RuntimeHelpers.Collection.RegisterInstance
                );
                InsertRegisteredInstanceInitializationCall(
                    method: GetOrEmitMethodWithBaseCall(implementedBy, "OnDisable"),
                    type: registeredAs,
                    helperMethod: MethodInfos.RuntimeHelpers.Collection.UnregisterInstance
                );
            }

            foreach (var type in context.Types)
            {
                try
                {
                    // inner try to rethrow unexpected exceptions as CriticalException
                    try
                    {
                        bool registerAll = type.HasAttribute<Register.All>();
                        bool registerSingle = type.HasAttribute<Register.Single>();

                        if (registerAll && registerSingle)
                            throw new MedicineError(
                                $"Type {type.FullName} shouldn't have both [Register.Single] and [Register.All] attributes.", type);

                        if (registerAll || registerSingle)
                        {
                            if (!type.IsInterface)
                                if (!type.DerivesFrom<MonoBehaviour>())
                                    if (!type.DerivesFrom<ScriptableObject>())
                                        throw new MedicineError($"Registered type needs to be a MonoBehaviour, a ScriptableObject or an interface.", type);

                            //todo: EnsureBaseOnEnableOnDisableAreCalled(type);
                        }

                        if (type.IsInterface)
                            continue;

                        if (registerAll)
                        {
                            // register type as itself
                            EmitCollectionTypeRegistration(registeredAs: type, implementedBy: type);

                            // register type as all interfaces it implements
                            foreach (var interfaceImplementation in type.Interfaces)
                                if (interfaceImplementation.InterfaceType.ResolveFast() is var interfaceType)
                                    if (interfaceType.HasAttribute<Register.All>())
                                        EmitCollectionTypeRegistration(registeredAs: interfaceImplementation.InterfaceType, implementedBy: type);
                        }

                        if (registerSingle)
                        {
                            // register type as itself
                            EmitSingletonTypeRegistration(registeredAs: type, implementedBy: type);

                            // register type as all interfaces it implements
                            foreach (var interfaceImplementation in type.Interfaces)
                                if (interfaceImplementation.InterfaceType.ResolveFast() is var interfaceType)
                                    if (interfaceType.HasAttribute<Register.Single>())
                                        EmitSingletonTypeRegistration(registeredAs: interfaceImplementation.InterfaceType, implementedBy: type);

                            // add [DefaultExecutionOrder(order: -1)] attribute to the registered singleton type
                            // this ensures that when the scene is loaded and scripts are initialized, the singleton registers itself before it is used by other scripts
                            if (!type.IsInterface && !type.HasAttribute<DefaultExecutionOrder>())
                            {
                                var defaultExecutionOrderAttribute = new CustomAttribute(MethodInfos.DefaultExecutionOrderConstructor.Import());
                                var constructorArgument = new CustomAttributeArgument(type: context.Module.TypeSystem.Int32, value: -1);
                                defaultExecutionOrderAttribute.ConstructorArguments.Add(constructorArgument);
                                type.CustomAttributes.Add(defaultExecutionOrderAttribute);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new MedicineError(ex.ToString(), type);
                    }
                }
                catch (MedicineWarning ex)
                {
                    context.DiagnosticMessages.Add(ex.Site.GetDiagnosticMessage(ex.Message));
                }
                catch (MedicineError ex)
                {
                    context.DiagnosticMessages.Add(ex.Site.GetDiagnosticMessage(ex.Message, DiagnosticType.Error));
                    return;
                }
            }
        }

        MethodDefinition EmitMethod(
            TypeDefinition declaringType,
            string methodName,
            MethodAttributes methodAttributes = MethodAttributes.Private | MethodAttributes.HideBySig,
            TypeReference returnType = null)
        {
            returnType = returnType ?? context.Module.TypeSystem.Void;
            var method = new MethodDefinition(methodName, methodAttributes, returnType);
            declaringType.Methods.Add(method);
            return method;
        }

        MethodDefinition GetOrEmitInitializationMethod(TypeDefinition declaringType, string methodName, MethodDefinition callSite)
        {
            MethodDefinition GetMatchingMethod(Collection<MethodDefinition> candidateMethods)
            {
                for (int i = 0, n = candidateMethods.Count; i < n; i++)
                    if (candidateMethods[i] is var candidate)
                        if (candidate.Name == methodName)
                            return candidate;

                return null;
            }

            MethodDefinition Emit()
            {
                // create injection method
                var injectionMethod = EmitMethod(declaringType, methodName, MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.Virtual);

                // emit injection method body
                {
                    injectionMethod.Body.Variables.Add(new VariableDefinition(typeof(GameObject).Import()));

                    var il = injectionMethod.Body.GetILProcessor();

                    // push "this" ref on top of the stack
                    il.Emit(OpCodes.Ldarg_0);

                    // invoke ".gameObject" getter
                    il.Emit(OpCodes.Call, MethodInfos.GetGameObject.Import());

                    // store GameObject ref in loc0
                    // - we're doing this because it's more efficient to call GetComponent methods on the
                    //   GameObject instead of the component instance
                    // - storing and re-using the GameObject in a local lets us save a few cycles here and
                    //   there by avoiding extern call overhead
                    il.Emit(OpCodes.Stloc_0);

                    /* initialization code will be inserted here by InsertInjectionCall */

                    il.Emit(OpCodes.Ret);
                }

                // emit the call to the injection method at target callsite
                {
                    var il = callSite.Body.Instructions;
                    int index = 0;
                    il.Insert(index++, Create(OpCodes.Ldarg_0));
                    il.Insert(index++, Create(OpCodes.Call, injectionMethod));
                }
                
                // implement IMedicineInjection interface
                // this lets us check if the component supports initialization and call it virtually
                var injectInterfaceMethod = MethodInfos.IMedicineInjectionInject.Import();
                injectionMethod.Overrides.Add(injectInterfaceMethod); // explicit implementation
                declaringType.Interfaces.Add(new InterfaceImplementation(injectInterfaceMethod.DeclaringType));

                return injectionMethod;
            }

            return GetMatchingMethod(declaringType.Methods) ?? Emit();
        }

        MethodDefinition GetOrEmitMethodWithBaseCall(TypeDefinition declaringType, string methodName)
        {
            MethodDefinition GetMatchingMethod(Collection<MethodDefinition> candidateMethods)
            {
                if (candidateMethods == null)
                    return null;

                for (int i = 0, n = candidateMethods.Count; i < n; i++)
                    if (candidateMethods[i] is var candidate)
                        if (candidate.Parameters.Count == 0)
                            if (candidate.Name == methodName)
                                if (candidate.ReturnType.FullName == "System.Void")
                                    return candidate;

                return null;
            }

            MethodDefinition Emit()
            {
                MethodDefinition GetBaseMethod()
                    => GetMatchingMethod(declaringType.BaseType?.ResolveFast()?.Methods);

                var method = EmitMethod(declaringType, methodName);
                var il = method.Body.GetILProcessor();

                // emit base method call
                if (GetBaseMethod() is MethodDefinition baseMethod)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, baseMethod.Import());
                }

                il.Emit(OpCodes.Ret);
                return method;
            }

            return GetMatchingMethod(declaringType.Methods) ?? Emit();
        }

        static MethodInfo GetInitializationMethodInfo(PropertyDefinition property, CustomAttribute attr)
        {
            var attrType = attr.AttributeType;
            bool includeInactive = attr.HasProperty(nameof(Inject.FromChildren.IncludeInactive), true);

            if (property.PropertyType.IsArray)
            {
                if (attrType.Is<Inject>())
                    return MethodInfos.RuntimeHelpers.InjectArray;

                if (attrType.Is<Inject.FromChildren>())
                    return includeInactive
                        ? MethodInfos.RuntimeHelpers.InjectFromChildrenArrayIncludeInactive
                        : MethodInfos.RuntimeHelpers.InjectFromChildrenArray;

                if (attrType.Is<Inject.FromParents>())
                    return includeInactive
                        ? MethodInfos.RuntimeHelpers.InjectFromParentsArrayIncludeInactive
                        : MethodInfos.RuntimeHelpers.InjectFromParentsArray;

                if (attrType.Is<Inject.Lazy>())
                    return MethodInfos.RuntimeHelpers.Lazy.InjectArray;

                if (attrType.Is<Inject.FromChildren.Lazy>())
                    return includeInactive
                        ? MethodInfos.RuntimeHelpers.Lazy.InjectFromChildrenArrayIncludeInactive
                        : MethodInfos.RuntimeHelpers.Lazy.InjectFromChildrenArray;

                if (attrType.Is<Inject.FromParents.Lazy>())
                    return includeInactive
                        ? MethodInfos.RuntimeHelpers.Lazy.InjectFromParentsArrayIncludeInactive
                        : MethodInfos.RuntimeHelpers.Lazy.InjectFromParentsArray;
            }
            else
            {
                if (attrType.Is<Inject>() || attrType.Is<Inject.Lazy>())
                    return MethodInfos.RuntimeHelpers.Inject;

                if (attrType.Is<Inject.FromChildren>() || attrType.Is<Inject.FromChildren.Lazy>())
                    return includeInactive
                        ? MethodInfos.RuntimeHelpers.InjectFromChildrenIncludeInactive
                        : MethodInfos.RuntimeHelpers.InjectFromChildren;

                if (attrType.Is<Inject.FromParents>() || attrType.Is<Inject.FromParents.Lazy>())
                    return includeInactive
                        ? MethodInfos.RuntimeHelpers.InjectFromParentsIncludingInactive
                        : MethodInfos.RuntimeHelpers.InjectFromParents;
            }

            throw new MedicineWarning($"Unknown injection attribute: {attrType.FullName.Replace('/', '.')}", property);
        }

        /// <summary> Emits instructions that initialize the property value. </summary>
        static void InsertInitializationCall(MethodDefinition method, FieldDefinition field, PropertyDefinition property, CustomAttribute attribute, MethodInfo initializationMethodInfo)
        {
            var il = method.Body.GetILProcessor();
            bool isOptional = attribute.HasProperty(nameof(Inject.Optional), value: true);
            bool isArray = field.FieldType.IsArray;

            var typeOrElementType = field.FieldType.UnwrapArrayElementType();

            var lastInstruction = method.Body.Instructions.Last();

            // remove last instruction (ret) so we can append instructions cheaply - we'll re-insert it at the end
            il.Body.Instructions.RemoveAt(method.Body.Instructions.Count - 1);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);

            // load cached "gameObject" local (first arg to the initialization method)
            il.Emit(OpCodes.Ldloc_0);

            // call initialization method specific to attribute
            il.Emit(OpCodes.Call, initializationMethodInfo.Import().MakeGenericInstanceMethod(typeOrElementType));

            // store result in field
            il.Emit(OpCodes.Stfld, field);

            // emit safety checks
            if (isOptional)
            {
                il.Emit(OpCodes.Pop);
            }
            else
            {
                // create target that we'll jump to if safety checks succeed
                // we'll append this instruction later
                var branchTarget = Create(OpCodes.Nop);

                // load value we stored in the field
                il.Emit(OpCodes.Ldfld, field);

                if (isArray)
                {
                    // call helper method to determine whether the array contains at least one element
                    il.Emit(OpCodes.Call, MethodInfos.RuntimeHelpers.ValidateArray.Import());
                }
                else
                {
                    // call UnityEngine.Object implicit bool operator to check if object is alive (exists, not destroyed, etc)
                    il.Emit(OpCodes.Call, MethodInfos.UnityObjectBoolOpImplicit.Import());
                }

                // branch to end if result on the stack is true 
                il.Emit(OpCodes.Brtrue, branchTarget);

                // push error string to stack (generated at compile-time specifically for this property)
                il.Emit(
                    OpCodes.Ldstr,
                    $"Failed to initialize <b>{property.PropertyType.Name} <i>{property.Name}</i></b> in component <b>{method.DeclaringType.FullName}</b>.\n" +
                    $"<i><b>[{attribute.AttributeType.FullName.Replace('/', '.')}]</b></i> Init(){property.GetMethod.DebugInformation.GetFilenameLineColumnString()}\n"
                );

                // push "this" to stack (last argument to UnityEngine.Debug.LogError, enables navigation to context object in the console window)
                il.Emit(OpCodes.Ldarg_0);

                // invoke UnityEngine.Debug.LogError
                il.Emit(OpCodes.Call, MethodInfos.LogError.Import());

                // append branch target
                il.Append(branchTarget);
            }

            // re-insert last instruction (ret) at the end
            il.Append(lastInstruction);
        }

        [SuppressMessage("ReSharper", "RedundantAssignment")]
        static void InsertRegisteredInstanceInitializationCall(MethodDefinition method, TypeReference type, MethodInfo helperMethod)
        {
            var il = method.Body.Instructions;

            // create reference to the registeration method on a generic instance of the helper type 
            var registerMethod = helperMethod.Import();

            if (helperMethod.DeclaringType.ContainsGenericParameters)
                registerMethod = registerMethod.MakeHostInstanceGeneric(type);

            int index = 0;

            // load "this" (first/only argument to registeration methods)
            il.Insert(index++, Create(OpCodes.Ldarg_0));

            // call registeration method
            il.Insert(index++, Create(OpCodes.Call, registerMethod));
        }

        void ReplacePropertyGetterWithHelperMethod(TypeDefinition type, PropertyDefinition property, MethodInfo helperMethod, bool requireGameObjectArg = false)
        {
            var getMethod = property.GetMethod;

            // get instruction that loads the field
            var ldfld = getMethod.Body.Instructions.Single(x => x.OpCode == OpCodes.Ldfld || x.OpCode == OpCodes.Ldsfld);

            // remove compiler-generated backing field
            type.Fields.Remove((ldfld.Operand as FieldReference).ResolveFast());

            // create reference to the helper method on a generic instance of the helper type
            var method = helperMethod.Import();

            if (helperMethod.DeclaringType.ContainsGenericParameters)
                method = method.MakeHostInstanceGeneric(property.PropertyType.UnwrapArrayElementType());
            else if (helperMethod.ContainsGenericParameters)
                method = method.MakeGenericInstanceMethod(property.PropertyType.UnwrapArrayElementType());

            var il = getMethod.Body.GetILProcessor();
            il.Body.Instructions.Clear();

            // get GameObject argument by calling .gameObject getter
            if (requireGameObjectArg)
            {
                // push "this" on top of the stack
                il.Emit(OpCodes.Ldarg_0);

                // invoke ".gameObject" getter
                il.Emit(OpCodes.Call, MethodInfos.GetGameObject.Import());
            }

            // emit helper method call (returns the reference to the object we're interested in)
            il.Emit(OpCodes.Call, method);

            il.Emit(OpCodes.Ret);
        }
    }
}
