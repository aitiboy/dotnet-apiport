﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ApiPortVS.Analyze;
using ApiPortVS.Resources;
using Autofac;
using EnvDTE;
using Microsoft.Fx.Portability;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ApiPortVS
{
    internal class AnalyzeMenu
    {
        private readonly TextWriter _output;
        private readonly DTE _dte;
        private readonly ILifetimeScope _scope;

        public AnalyzeMenu(ILifetimeScope scope, DTE dte, TextWriter output)
        {
            _scope = scope;
            _dte = dte;
            _output = output;
        }

        public async Task AnalyzeSelectedProjectsAsync(bool includeDependencies)
        {
            var projects = GetSelectedProjects();

            await AnalyzeProjectsAsync(includeDependencies ? GetTransitiveReferences(projects, new HashSet<Project>()) : projects);
        }

        public async void SolutionContextMenuItemCallback(object sender, EventArgs e)
        {
            await AnalyzeProjectsAsync(_dte.Solution.GetProjects().ToList());
        }

        private async Task AnalyzeProjectsAsync(ICollection<Project> projects)
        {
            if (!projects.Any())
            {
                return;
            }

            try
            {
                WriteHeader();

                using (var innerScope = _scope.BeginLifetimeScope())
                {
                    var projectAnalyzer = innerScope.Resolve<ProjectAnalyzer>();

                    await projectAnalyzer.AnalyzeProjectAsync(projects);
                }
            }
            catch (PortabilityAnalyzerException ex)
            {
                _output.WriteLine();
                _output.WriteLine(ex.Message);
            }
            catch (Exception ex)
            {
                _output.WriteLine();
                _output.WriteLine(LocalizedStrings.UnknownError);

                Trace.WriteLine(ex);
            }
            finally
            {
                _output.WriteLine();
            }
        }

        public void ProjectContextMenuItemBeforeQueryStatus(object sender, EventArgs e)
        {
            ContextMenuItemBeforeQueryStatus(sender, GetSelectedProjects(), false);
        }

        public void ProjectContextMenuDependenciesItemBeforeQueryStatus(object sender, EventArgs e)
        {
            ContextMenuItemBeforeQueryStatus(sender, GetSelectedProjects(), true);
        }

        public void SolutionContextMenuItemBeforeQueryStatus(object sender, EventArgs e)
        {
            ContextMenuItemBeforeQueryStatus(sender, _dte.Solution.GetProjects(), false);
        }

        private void ContextMenuItemBeforeQueryStatus(object sender, IEnumerable<Project> projects, bool checkDependencies)
        {
            var menuItem = sender as Microsoft.VisualStudio.Shell.OleMenuCommand;
            if (menuItem == null)
            {
                return;
            }

            menuItem.Visible = projects.Any() && projects.All(p => p.IsDotNetProject());

            // Only need to check dependents if menuItem is still visible
            if (checkDependencies && menuItem.Visible)
            {
                menuItem.Visible = projects.SelectMany(p => p.GetReferences()).Any();
            }
        }

        public async void AnalyzeMenuItemCallback(object sender, EventArgs e)
        {
            var inputAssemblyPaths = PromptForAssemblyPaths();
            if (inputAssemblyPaths == null)
            {
                return;
            }

            try
            {
                WriteHeader();

                using (var innerScope = _scope.BeginLifetimeScope())
                {
                    var fileListAnalyzer = innerScope.Resolve<FileListAnalyzer>();

                    await fileListAnalyzer.AnalyzeProjectAsync(inputAssemblyPaths);
                }
            }
            catch (PortabilityAnalyzerException ex)
            {
                _output.WriteLine();
                _output.WriteLine(ex.Message);
            }
            catch (Exception ex)
            {
                _output.WriteLine();
                _output.WriteLine(LocalizedStrings.UnknownError);

                Trace.WriteLine(ex);
            }
            finally
            {
                _output.WriteLine();
            }
        }

        private string[] PromptForAssemblyPaths()
        {
            var openDialog = new OpenFileDialog
            {
                CheckFileExists = true,
                CheckPathExists = true,
                Filter = LocalizedStrings.AssemblyFiles + "|*.exe;*.dll",
                Multiselect = true,
                Title = LocalizedStrings.SelectAssemblyFiles
            };

            return (bool)openDialog.ShowDialog() ? openDialog.FileNames : null;
        }

        private void WriteHeader()
        {
            var header = new StringBuilder();
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            header.AppendLine(string.Format(LocalizedStrings.CopyrightFormat, assemblyVersion));
            header.AppendLine(string.Concat(LocalizedStrings.MoreInformationAvailableAt, " ", LocalizedStrings.MoreInformationUrl));

            _output.WriteLine(header.ToString());
        }

        private ICollection<Project> GetSelectedProjects()
        {
            return _dte.SelectedItems
                .OfType<SelectedItem>()
                .Select(i => i.Project)
                .Distinct()
                .ToList();
        }

        private ICollection<Project> GetTransitiveReferences(IEnumerable<Project> projects, HashSet<Project> expandedProjects)
        {
            foreach (var project in projects)
            {
                if (expandedProjects.Add(project))
                {
                    GetTransitiveReferences(project.GetReferences(), expandedProjects);
                }
            }

            return expandedProjects;
        }
    }
}
