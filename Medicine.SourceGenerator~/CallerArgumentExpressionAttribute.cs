namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Allows developers to capture the expressions passed to a method to enable
    /// better error messages in diagnostic/testing APIs.
    /// </summary>
    /// <seealso href="https://weblogs.asp.net/dixin/csharp-10-new-feature-callerargumentexpression-argument-check-and-more"/>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public string ParameterName { get; }

        /// <param name="parameterName">The name of the parameter to capture.</param>
        public CallerArgumentExpressionAttribute(string parameterName)
            => ParameterName = parameterName;
    }
}