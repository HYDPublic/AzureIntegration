// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Azure.Management.AppService.Fluent;
using Microsoft.Azure.Management.AppService.Fluent.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.AzureAppServices.FunctionalTests
{
    internal static class WebAppExtensions
    {
        public static HttpClient CreateClient(this IWebApp site)
        {
            var domain = site.GetHostNameBindings().First().Key;

            return new HttpClient { BaseAddress = new Uri("http://" + domain) };
        }

        public static async Task UploadFilesAsync(this IWebApp site, DirectoryInfo from, string to, IPublishingProfile publishingProfile, ILogger logger)
        {
            foreach (var info in from.GetFileSystemInfos("*"))
            {
                var address = new Uri(
                    "ftp://" + publishingProfile.FtpUrl + to + info.FullName.Substring(from.FullName.Length + 1).Replace('\\', '/'));

                if (info is FileInfo file)
                {
                    logger.LogInformation($"Uploading {file.FullName} to {address}");

                    var request = CreateRequest(publishingProfile, address, WebRequestMethods.Ftp.UploadFile);
                    using (var fileStream = File.OpenRead(file.FullName))
                    {
                        using (var requestStream = await request.GetRequestStreamAsync())
                        {
                            await fileStream.CopyToAsync(requestStream);
                        }
                    }
                    await request.GetResponseAsync();
                }
                if (info is DirectoryInfo directory)
                {
                    var request = CreateRequest(publishingProfile, address, WebRequestMethods.Ftp.MakeDirectory);
                    await request.GetResponseAsync();
                    await UploadFilesAsync(site, directory, to + directory.Name + '/', publishingProfile, logger);
                }
            }
        }

        private static FtpWebRequest CreateRequest(IPublishingProfile publishingProfile, Uri address, string method)
        {
            var request = (FtpWebRequest) WebRequest.Create(address);
            request.Method = method;
            request.KeepAlive = true;
            request.UseBinary = true;
            request.UsePassive = false;
            request.Credentials = new NetworkCredential(publishingProfile.FtpUsername, publishingProfile.FtpPassword);
            request.ConnectionGroupName = "group";
            return request;
        }

        public static async Task GitDeploy(this IWebApp site, DirectoryInfo workingDirectory, ILogger logger)
        {
            var git = new TestCommand("git")
            {
                Logger = logger,
                WorkingDirectory = workingDirectory.FullName
            };

            var publishingProfile = await site.GetPublishingProfileAsync();

            await git.ExecuteAndAssertAsync("init");
            await git.ExecuteAndAssertAsync($"remote add origin https://{publishingProfile.GitUsername}:{publishingProfile.GitPassword}@{publishingProfile.GitUrl}");
            await git.ExecuteAndAssertAsync("add .");
            await git.ExecuteAndAssertAsync("commit -am Initial");
            await git.ExecuteAndAssertAsync("push origin master");
        }

        public static async Task BuildPublishProfileAsync(this IWebApp site, string projectDirectory)
        {
            var result = await site.Manager.WebApps.Inner.ListPublishingProfileXmlWithSecretsAsync(
                site.ResourceGroupName,
                site.Name,
                new CsmPublishingProfileOptionsInner());

            var targetDirectory = Path.Combine(projectDirectory, "Properties", "PublishProfiles");
            Directory.CreateDirectory(targetDirectory);

            var publishSettings = XDocument.Load(result);
            foreach (var profile in publishSettings.Root.Elements("publishProfile"))
            {
                if ((string) profile.Attribute("publishMethod") == "MSDeploy")
                {
                    new XDocument(
                        new XElement("Project",
                            new XElement("PropertyGroup",
                                new XElement("WebPublishMethod", "MSDeploy"),
                                new XElement("PublishProvider", "AzureWebSite"),
                                new XElement("UserName", (string)profile.Attribute("userName")),
                                new XElement("Password", (string)profile.Attribute("userPWD")),
                                new XElement("MSDeployServiceURL", (string)profile.Attribute("publishUrl")),
                                new XElement("DeployIisAppPath", (string)profile.Attribute("msdeploySite"))
                            )))
                        .Save(Path.Combine(targetDirectory, "Profile.pubxml"));
                }
            }
        }
    }
}