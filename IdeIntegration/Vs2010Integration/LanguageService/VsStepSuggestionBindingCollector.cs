﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EnvDTE;
using EnvDTE80;
using TechTalk.SpecFlow.Bindings;
using TechTalk.SpecFlow.Bindings.Reflection;
using TechTalk.SpecFlow.Configuration;
using TechTalk.SpecFlow.Vs2010Integration.Utils;

namespace TechTalk.SpecFlow.Vs2010Integration.LanguageService
{
    internal class VsStepSuggestionBindingCollector
    {
        private readonly VsBindingReflectionFactory bindingReflectionFactory = new VsBindingReflectionFactory();
        private readonly IStepDefinitionRegexCalculator regexCalculator;

        public VsStepSuggestionBindingCollector()
        {
            regexCalculator = new StepDefinitionRegexCalculator(new RuntimeConfiguration()); //TODO
        }

        public IEnumerable<StepDefinitionBinding> GetBindingsFromProjectItem(ProjectItem projectItem)
        {
            foreach (CodeClass bindingClassWithBindingAttribute in VsxHelper.GetClasses(projectItem).Where(IsBindingClass))
            {
                BindingScope[] bindingScopes = GetClassScopes(bindingClassWithBindingAttribute);

                CodeClass2 bindingClassIncludingParts = bindingClassWithBindingAttribute as CodeClass2;
                if (bindingClassIncludingParts == null)
                {
                    foreach (StepDefinitionBinding currrentFoundStep in GetStepsFromClass(bindingClassWithBindingAttribute, bindingScopes))
                    {
                        yield return currrentFoundStep;
                    }
                }
                else
                {
                    foreach (CodeClass2 currentBindingPartialClass in bindingClassIncludingParts.Parts)
                    {
                        foreach (StepDefinitionBinding currentPartialClassStep in GetStepsFromClass(currentBindingPartialClass as CodeClass, bindingScopes))
                        {
                            yield return currentPartialClassStep;
                        }
                    }
                }
            }
        }

        private BindingScope[] GetClassScopes(CodeClass codeClass)
        {
            return codeClass.Attributes.Cast<CodeAttribute2>().Select(GetBingingScopeFromAttribute).Where(s => s != null).ToArray();
        }

        private IEnumerable<StepDefinitionBinding> GetStepsFromClass(CodeClass codeClass, BindingScope[] classScopes)
        {
            return codeClass.Children.OfType<CodeFunction>().SelectMany(codeFunction => GetSuggestionsFromCodeFunction(codeFunction, classScopes));
        }

        static public bool IsBindingClass(CodeClass codeClass)
        {
            try
            {
                return codeClass.Attributes.Cast<CodeAttribute>().Any(attr => typeof(BindingAttribute).FullName.Equals(attr.FullName));
            }
            catch(Exception)
            {
                return false;
            }
        }

        private IEnumerable<StepDefinitionBinding> GetSuggestionsFromCodeFunction(CodeFunction codeFunction, IEnumerable<BindingScope> classBindingScopes)
        {
            var bindingScopes = classBindingScopes.Concat(codeFunction.Attributes.Cast<CodeAttribute2>().Select(GetBingingScopeFromAttribute).Where(s => s != null)).ToArray();

            if (bindingScopes.Any())
            {
                foreach (var bindingScope in bindingScopes)
                {
                    foreach (var stepBinding in GetSuggestionsFromCodeFunctionForScope(codeFunction, bindingScope))
                    {
                        yield return stepBinding;
                    }
                }
            }
            else
            {
                foreach (var stepBinding in GetSuggestionsFromCodeFunctionForScope(codeFunction, null))
                {
                    yield return stepBinding;
                }
            }
        }

        private IEnumerable<StepDefinitionBinding> GetSuggestionsFromCodeFunctionForScope(CodeFunction codeFunction, BindingScope bindingScope)
        {
            return codeFunction.Attributes.Cast<CodeAttribute2>()
                .SelectMany(codeAttribute => GetStepDefinitionsFromAttribute(codeAttribute, codeFunction, bindingScope))
                .Where(binding => binding != null);
        }

        private string GetStringArgumentValue(CodeAttribute2 codeAttribute, string argumentName)
        {
            var arg = codeAttribute.Arguments.Cast<CodeAttributeArgument>().FirstOrDefault(a => a.Name == argumentName);
            if (arg == null)
                return null;

            return VsxHelper.ParseCodeStringValue(arg.Value, arg.Language);
        }

        private BindingScope GetBingingScopeFromAttribute(CodeAttribute2 codeAttribute)
        {
            try
            {
                if (IsScopeAttribute(codeAttribute))
                {
                    var tag = GetStringArgumentValue(codeAttribute, "Tag");
                    string feature = GetStringArgumentValue(codeAttribute, "Feature");
                    string scenario = GetStringArgumentValue(codeAttribute, "Scenario");

                    if (tag == null && feature == null && scenario == null)
                        return null;

                    return new BindingScope(tag, feature, scenario);
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool IsScopeAttribute(CodeAttribute2 codeAttribute)
        {
            return 
                codeAttribute.FullName.Equals(typeof(ScopeAttribute).FullName) ||
#pragma warning disable 612,618
                codeAttribute.FullName.Equals(typeof(StepScopeAttribute).FullName);
#pragma warning restore 612,618
        }

        private IEnumerable<StepDefinitionBinding> GetStepDefinitionsFromAttribute(CodeAttribute2 codeAttribute, CodeFunction codeFunction, BindingScope bindingScope)
        {
            var normalStepDefinition =
                GetBingingFromAttribute(codeAttribute, codeFunction, StepDefinitionType.Given, bindingScope) ??
                GetBingingFromAttribute(codeAttribute, codeFunction, StepDefinitionType.When, bindingScope) ??
                GetBingingFromAttribute(codeAttribute, codeFunction, StepDefinitionType.Then, bindingScope);
            if (normalStepDefinition != null)
            {
                yield return normalStepDefinition;
                yield break;
            }

            if (IsGeneralStepDefinition(codeAttribute))
            {
                yield return CreateStepBinding(codeAttribute, codeFunction, StepDefinitionType.Given, bindingScope);
                yield return CreateStepBinding(codeAttribute, codeFunction, StepDefinitionType.When, bindingScope);
                yield return CreateStepBinding(codeAttribute, codeFunction, StepDefinitionType.Then, bindingScope);
            }
        }

        private StepDefinitionBinding GetBingingFromAttribute(CodeAttribute2 codeAttribute, CodeFunction codeFunction, StepDefinitionType stepDefinitionType, BindingScope bindingScope)
        {
            try
            {
                if (codeAttribute.FullName.Equals(string.Format("TechTalk.SpecFlow.{0}Attribute", stepDefinitionType)))
                    return CreateStepBinding(codeAttribute, codeFunction, stepDefinitionType, bindingScope);
                return null;
            }
            catch(Exception)
            {
                return null;
            }
        }

        private bool IsGeneralStepDefinition(CodeAttribute2 codeAttribute)
        {
            try
            {
                return codeAttribute.FullName.Equals(typeof (StepDefinitionAttribute).FullName);
            }
            catch(Exception)
            {
                return false;
            }
        }

        private StepDefinitionBinding CreateStepBinding(CodeAttribute2 attr, CodeFunction codeFunction, StepDefinitionType stepDefinitionType, BindingScope bindingScope)
        {
            try
            {
                IBindingMethod bindingMethod = bindingReflectionFactory.CreateBindingMethod(codeFunction);
                string regexString;

                var regexArg = attr.Arguments.Cast<CodeAttributeArgument>().FirstOrDefault();
                if (regexArg != null)
                    regexString = VsxHelper.ParseCodeStringValue(regexArg.Value, regexArg.Language);
                else
                    regexString = regexCalculator.CalculateRegexFromMethod(stepDefinitionType, bindingMethod);

                return new StepDefinitionBinding(stepDefinitionType, regexString, bindingMethod, bindingScope);
            }
            catch(Exception)
            {
                return null;
            }
        }

        public CodeFunction FindCodeFunction(VsProjectScope projectScope, IBindingMethod bindingMethod)
        {
            var project = projectScope.Project;

            var function = FindCodeFunction(project, bindingMethod);
            if (function != null)
                return function;

            var specFlowProject = projectScope.SpecFlowProjectConfiguration;
            if (specFlowProject != null)
            {
                foreach (var assemblyName in specFlowProject.RuntimeConfiguration.AdditionalStepAssemblies)
                {
                    string simpleName = assemblyName.Split(new[] { ',' }, 2)[0];

                    var stepProject = VsxHelper.FindProjectByAssemblyName(project.DTE, simpleName);
                    if (stepProject != null)
                    {
                        function = FindCodeFunction(stepProject, bindingMethod);
                        if (function != null)
                            return function;
                    }
                }
            }

            return null;
        }

        private CodeFunction FindCodeFunction(Project project, IBindingMethod bindingMethod)
        {
            return GetBindingClassesIncludingPartialClasses(project)
                .Where(c => c.FullName == bindingMethod.Type.FullName)
                .SelectMany(c => c.GetFunctions()).FirstOrDefault(
                    f => f.Name == bindingMethod.Name && BindingReflectionExtensions.MethodEquals(bindingMethod, bindingReflectionFactory.CreateBindingMethod(f)));
        }

        private IEnumerable<CodeClass> GetBindingClassesIncludingPartialClasses(Project project)
        {
            foreach (CodeClass bindingClassWithBindingAttribute in VsxHelper.GetClasses(project).Where(IsBindingClass))
            {
                yield return bindingClassWithBindingAttribute;

                CodeClass2 bindingClassIncludingParts = bindingClassWithBindingAttribute as CodeClass2;
                foreach (CodeClass2 currentBindingPartialClass in bindingClassIncludingParts.Parts)
                {
                    yield return currentBindingPartialClass as CodeClass;
                }
            }
        }
    }
}