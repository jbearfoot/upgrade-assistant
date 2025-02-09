﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.DotNet.UpgradeAssistant.Extensions;
using Microsoft.DotNet.UpgradeAssistant.Steps.ProjectFormat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.UpgradeAssistant
{
    public static class ProjectFormatStepsExtensions
    {
        public static OptionsBuilder<TryConvertOptions> AddProjectFormatSteps(this IExtensionServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.Services.AddUpgradeStep<SetTFMStep>();

            if (FeatureFlags.IsSolutionWideSdkConversionEnabled)
            {
                services.Services.AddUpgradeStep<SdkStyleConversionSolutionWideStep>();
            }
            else
            {
                services.Services.AddUpgradeStep<TryConvertProjectConverterStep>();
            }

            services.Services.AddTransient<ITryConvertTool, TryConvertInProcessTool>();
            services.Services.AddTransient<TryConvertRunner>();

            return services.Services.AddOptions<TryConvertOptions>()
                .PostConfigure(options =>
                {
                    var path = Environment.ExpandEnvironmentVariables(options.ToolPath);

                    if (!Path.IsPathRooted(path))
                    {
                        var fileInfo = services.Files.GetFileInfo(options.ToolPath);

                        if (fileInfo.Exists && fileInfo.PhysicalPath is string physicalPath)
                        {
                            path = physicalPath;
                        }
                    }

                    options.ToolPath = path;
                })
                .ValidateDataAnnotations();
        }
    }
}
