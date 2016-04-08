// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Input;
using Microsoft.PythonTools.EnvironmentsList;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Project;
using Microsoft.PythonTools.Repl;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.InteractiveWindow.Shell;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.PythonTools.InterpreterList {
    [Guid(PythonConstants.InterpreterListToolWindowGuid)]
    sealed class InterpreterListToolWindow : ToolWindowPane {
        private IServiceProvider _site;
        private PythonToolsService _pyService;
        private Redirector _outputWindow;
        private IVsStatusbar _statusBar;

        public InterpreterListToolWindow() { }

        protected override void OnCreate() {
            base.OnCreate();

            _site = (IServiceProvider)this;

            _pyService = _site.GetPythonToolsService();

            // TODO: Get PYEnvironment added to image list
            BitmapImageMoniker = KnownMonikers.DockPanel;
            Caption = Strings.Environments;

            _outputWindow = OutputWindowRedirector.GetGeneral(_site);
            Debug.Assert(_outputWindow != null);
            _statusBar = _site.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
            
            var list = new ToolWindow();
            list.Site = _site;
            list.ViewCreated += List_ViewCreated;

            list.CommandBindings.Add(new CommandBinding(
                EnvironmentView.OpenInteractiveWindow,
                OpenInteractiveWindow_Executed,
                OpenInteractiveWindow_CanExecute
            ));
            list.CommandBindings.Add(new CommandBinding(
                EnvironmentPathsExtension.StartInterpreter,
                StartInterpreter_Executed,
                StartInterpreter_CanExecute
            ));
            list.CommandBindings.Add(new CommandBinding(
                EnvironmentPathsExtension.StartWindowsInterpreter,
                StartInterpreter_Executed,
                StartInterpreter_CanExecute
            ));
            list.CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Help,
                OnlineHelp_Executed,
                OnlineHelp_CanExecute
            ));
            list.CommandBindings.Add(new CommandBinding(
                ToolWindow.UnhandledException,
                UnhandledException_Executed,
                UnhandledException_CanExecute
            ));

            Content = list;
        }

        private void List_ViewCreated(object sender, EnvironmentViewEventArgs e) {
            var view = e.View;
            var pep = new PipExtensionProvider(view.Factory);
            pep.GetElevateSetting += PipExtensionProvider_GetElevateSetting;
            pep.OperationStarted += PipExtensionProvider_OperationStarted;
            pep.OutputTextReceived += PipExtensionProvider_OutputTextReceived;
            pep.ErrorTextReceived += PipExtensionProvider_ErrorTextReceived;
            pep.OperationFinished += PipExtensionProvider_OperationFinished;

            view.Extensions.Add(pep);
            var _withDb = view.Factory as PythonInterpreterFactoryWithDatabase;
            if (_withDb != null) {
                view.Extensions.Add(new DBExtensionProvider(_withDb));
            }

            var model = _site.GetComponentModel();
            if (model != null) {
                try {
                    foreach (var provider in model.GetExtensions<IEnvironmentViewExtensionProvider>()) {
                        try {
                            var ext = provider.CreateExtension(view);
                            if (ext != null) {
                                view.Extensions.Add(ext);
                            }
                        } catch (Exception ex) {
                            LogLoadException(provider, ex);
                        }
                    }
                } catch (Exception ex2) {
                    LogLoadException(null, ex2);
                }
            }
        }

        private void LogLoadException(IEnvironmentViewExtensionProvider provider, Exception ex) {
            string message;
            if (provider == null) {
                message = Strings.ErrorLoadingEnvironmentViewExtensions.FormatUI(ex);
            } else {
                message = Strings.ErrorLoadingEnvironmentViewExtension.FormatUI(provider.GetType().FullName, ex);
            }

            Debug.Fail(message);
            var log = _site.GetService(typeof(SVsActivityLog)) as IVsActivityLog;
            if (log != null) {
                log.LogEntry(
                    (uint)__ACTIVITYLOG_ENTRYTYPE.ALE_ERROR,
                    Strings.ProductTitle,
                    message
                );
            }
        }

        private void PipExtensionProvider_GetElevateSetting(object sender, ValueEventArgs<bool> e) {
            e.Value = _pyService.GeneralOptions.ElevatePip;
        }

        private void PipExtensionProvider_OperationStarted(object sender, ValueEventArgs<string> e) {
            _outputWindow.WriteLine(e.Value);
            if (_statusBar != null) {
                _statusBar.SetText(e.Value);
            }
            if (_pyService.GeneralOptions.ShowOutputWindowForPackageInstallation) {
                _outputWindow.ShowAndActivate();
            }
        }

        private void PipExtensionProvider_OutputTextReceived(object sender, ValueEventArgs<string> e) {
            _outputWindow.WriteLine(e.Value);
        }

        private void PipExtensionProvider_ErrorTextReceived(object sender, ValueEventArgs<string> e) {
            _outputWindow.WriteErrorLine(e.Value);
        }

        private void PipExtensionProvider_OperationFinished(object sender, ValueEventArgs<string> e) {
            _outputWindow.WriteLine(e.Value);
            if (_statusBar != null) {
                _statusBar.SetText(e.Value);
            }
            if (_pyService.GeneralOptions.ShowOutputWindowForPackageInstallation) {
                _outputWindow.ShowAndActivate();
            }
        }

        private void UnhandledException_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = e.Parameter is ExceptionDispatchInfo;
        }

        private void UnhandledException_Executed(object sender, ExecutedRoutedEventArgs e) {
            var ex = (ExceptionDispatchInfo)e.Parameter;
            Debug.Assert(ex != null, "Unhandled exception with no exception object");
            if (ex.SourceException is PipException) {
                // Don't report Pip exceptions. The output messages have
                // already been handled.
                return;
            }

            var td = TaskDialog.ForException(_site, ex.SourceException, String.Empty, PythonConstants.IssueTrackerUrl);
            td.Title = Strings.ProductTitle;
            td.ShowModal();
        }

        private void OpenInteractiveWindow_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var view = e.Parameter as EnvironmentView;
            e.CanExecute = view != null &&
                view.Factory != null && 
                view.Factory.Configuration != null &&
                File.Exists(view.Factory.Configuration.InterpreterPath);
        }

        private void OpenInteractiveWindow_Executed(object sender, ExecutedRoutedEventArgs e) {
            var view = (EnvironmentView)e.Parameter;
            var config = view.Factory.Configuration;

            var replId = PythonReplEvaluatorProvider.GetEvaluatorId(config);

            var compModel = _site.GetComponentModel();
            var service = compModel.GetService<InteractiveWindowProvider>();
            IVsInteractiveWindow window;

            // TODO: Figure out another way to get the project
            //var provider = _service.KnownProviders.OfType<LoadedProjectInterpreterFactoryProvider>().FirstOrDefault();
            //var vsProject = provider == null ?
            //    null :
            //    provider.GetProject(factory);
            //PythonProjectNode project = vsProject == null ? null : vsProject.GetPythonProject();
            try {
                window = service.OpenOrCreate(replId);
            } catch (Exception ex) when (!ex.IsCriticalException()) {
                MessageBox.Show(Strings.ErrorOpeningInteractiveWindow.FormatUI(ex), Strings.ProductTitle);
                return;
            }

            window?.Show(true);
        }

        private void StartInterpreter_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            var view = e.Parameter as EnvironmentView;
            e.CanExecute = view != null && File.Exists(e.Command == EnvironmentPathsExtension.StartInterpreter ?
                view.Factory.Configuration.InterpreterPath :
                view.Factory.Configuration.WindowsInterpreterPath);
            e.Handled = true;
        }

        private void StartInterpreter_Executed(object sender, ExecutedRoutedEventArgs e) {
            var view = (EnvironmentView)e.Parameter;
            var factory = view.Factory;

            var psi = new ProcessStartInfo();
            psi.UseShellExecute = false;

            psi.FileName = e.Command == EnvironmentPathsExtension.StartInterpreter ?
                factory.Configuration.InterpreterPath :
                factory.Configuration.WindowsInterpreterPath;
            psi.WorkingDirectory = factory.Configuration.PrefixPath;

            // TODO: Figure out some other wa to get the project
            //var provider = _service.KnownProviders.OfType<LoadedProjectInterpreterFactoryProvider>().FirstOrDefault();
            //var vsProject = provider == null ?
            //    null :
            //    provider.GetProject(factory);
            IPythonProject project = null;// vsProject == null ? null : vsProject.GetPythonProject();
            if (project != null) {
                psi.EnvironmentVariables[factory.Configuration.PathEnvironmentVariable] = 
                    string.Join(";", project.GetSearchPaths());
            } else {
                psi.EnvironmentVariables[factory.Configuration.PathEnvironmentVariable] = string.Empty;
            }

            Process.Start(psi);
        }

        private void OnlineHelp_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = _site != null;
            e.Handled = true;
        }

        private void OnlineHelp_Executed(object sender, ExecutedRoutedEventArgs e) {
            VisualStudioTools.CommonPackage.OpenVsWebBrowser(_site, PythonToolsPackage.InterpreterHelpUrl);
            e.Handled = true;
        }
    }
}
