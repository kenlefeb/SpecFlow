using System;
using System.Reflection;
using System.Linq;

namespace TechTalk.SpecFlow.Bindings.Reflection
{
    public class RuntimeBindingParameter : IBindingParameter
    {
        private readonly ParameterInfo parameterInfo;

        public IBindingType Type
        {
            get { return new RuntimeBindingType(parameterInfo.ParameterType); }
        }

        public string ParameterName
        {
            get { return parameterInfo.Name; }
        }

        public RuntimeBindingParameter(ParameterInfo parameterInfo)
        {
            this.parameterInfo = parameterInfo;
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", ParameterName, Type);
        }


        public bool IsParamArray
        {
            get 
            {
                var attribute = (parameterInfo == null) ? false : parameterInfo.GetCustomAttributes(typeof(ParamArrayAttribute), false).FirstOrDefault();
                return (attribute != null);
            }
        }
    }
}