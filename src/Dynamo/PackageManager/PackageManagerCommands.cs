﻿//Copyright © Autodesk, Inc. 2012. All rights reserved.
//
//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at
//
//http://www.apache.org/licenses/LICENSE-2.0
//
//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Dynamo.Controls;
using Dynamo.Nodes;
using Dynamo.PackageManager;
using Dynamo.Utilities;
using System.Windows.Controls;

namespace Dynamo.Commands
{
    public class LoginCommand : ICommand
    {
        public LoginCommand()
        {

        }

        public void Execute(object parameters)
        {
            
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameters)
        {
            return true;
        }
    }

    public class ShowNodePublishInfoCommand : ICommand
    {
        private bool init;
        private PackageManagerPublishView _view;

        public ShowNodePublishInfoCommand()
        {
            this.init = false;
        }

        public void Execute(object funcDef)
        {

            if (dynSettings.Controller.PackageManagerClient.IsLoggedIn == false)
            {
                DynamoCommands.ShowLoginCmd.Execute(null);
                dynSettings.Bench.Log("Must login first to publish a node.");
                return;
            }

            if (!init)
            {
                _view = new PackageManagerPublishView(dynSettings.Controller.PackageManagerPublishViewModel);
                dynSettings.Bench.outerCanvas.Children.Add(_view);
                Canvas.SetBottom(_view, 0);
                Canvas.SetRight(_view, 0);
                init = true;
            }
            
            if (funcDef is FunctionDefinition)
            {
                var f = funcDef as FunctionDefinition;

                dynSettings.Controller.PackageManagerPublishViewModel.FunctionDefinition =
                    f;

                // we're submitting a new version
                if ( dynSettings.Controller.PackageManagerClient.LoadedPackageHeaders.ContainsKey(f) )
                {
                    dynSettings.Controller.PackageManagerPublishViewModel.PackageHeader =
                        dynSettings.Controller.PackageManagerClient.LoadedPackageHeaders[f];
                }
            }
            else
            {
                dynSettings.Bench.Log("Failed to obtain function definition from node.");
                return;
            }
            
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameters)
        {
            return true;
        }
    }

    public class ShowLoginCommand : ICommand
    {
        private bool _init;

        public void Execute(object parameters)
        {
            if (!_init)
            {
                var loginView = new PackageManagerLoginView(dynSettings.Controller.PackageManagerLoginViewModel);
                dynSettings.Bench.outerCanvas.Children.Add(loginView);
                Canvas.SetBottom(loginView, 0);
                Canvas.SetRight(loginView, 0);
                _init = true;
            }

            dynSettings.Controller.PackageManagerLoginViewModel.Visible = Visibility.Visible;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameters)
        {
            return true;
        }
    }

    public class RefreshRemotePackagesCommand : ICommand
    {
        public void Execute(object parameters)
        {
            dynSettings.Controller.PackageManagerClient.RefreshAvailable();
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameters)
        {
            return true;
        }
    }

    public class PublishSelectedNodeCommand : ICommand
    {
        private PackageManagerClient _client;

        public void Execute(object parameters)
        {
            this._client = dynSettings.Controller.PackageManagerClient;

            var nodeList = dynSettings.Bench.WorkBench.Selection.Where(x => x is dynNodeUI && ((dynNodeUI)x).NodeLogic is dynFunction )
                                        .Select(x => ( ((dynNodeUI)x).NodeLogic as dynFunction ).Definition.FunctionId ).ToList();

            if (nodeList.Count != 1)
            {
                MessageBox.Show("You must select a single user-defined node.  You selected " + nodeList.Count + " nodes." , "Selection Error", MessageBoxButton.OK, MessageBoxImage.Question);
                return;
            }

            if ( dynSettings.Controller.CustomNodeLoader.Contains( nodeList[0] ) )
            {
                DynamoCommands.ShowNodeNodePublishInfoCmd.Execute( dynSettings.Controller.CustomNodeLoader.GetFunctionDefinition( nodeList[0]) );
            }
            else
            {
                MessageBox.Show("The selected symbol was not found in the workspace", "Selection Error", MessageBoxButton.OK, MessageBoxImage.Question);
            }

        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameters)
        {
            // todo: should check authentication state, connected to internet
            return true;
        }
    }

    public class PublishCurrentWorkspaceCommand : ICommand
    {

        private PackageManagerClient _client;

        public void Execute(object parameters)
        {
            this._client = dynSettings.Controller.PackageManagerClient;
            
            if ( dynSettings.Controller.ViewingHomespace )
            {
                MessageBox.Show("You can't publish your the home workspace.", "Workspace Error", MessageBoxButton.OK, MessageBoxImage.Question);
                return;
            }

            var currentFunDef =
                dynSettings.Controller.CustomNodeLoader.GetDefinitionFromWorkspace(dynSettings.Controller.CurrentSpace);

            if ( currentFunDef != null )
            {
                DynamoCommands.ShowNodeNodePublishInfoCmd.Execute(currentFunDef);
            }
            else
            {
                MessageBox.Show("The selected symbol was not found in the workspace", "Selection Error", MessageBoxButton.OK, MessageBoxImage.Question);
            }

        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameters)
        {
            return true;
        }
    }
    
    public static partial class DynamoCommands
    {

        private static ShowNodePublishInfoCommand _showNodePublishInfoCmd;
        public static ShowNodePublishInfoCommand ShowNodeNodePublishInfoCmd
        {
            get
            {
                if (_showNodePublishInfoCmd == null)
                    _showNodePublishInfoCmd = new ShowNodePublishInfoCommand();
                return _showNodePublishInfoCmd;
            }
        }

        private static PublishCurrentWorkspaceCommand publishCurrentWorkspaceCmd;
        public static PublishCurrentWorkspaceCommand PublishCurrentWorkspaceCmd
        {
            get
            {
                if (publishCurrentWorkspaceCmd == null)
                    publishCurrentWorkspaceCmd = new PublishCurrentWorkspaceCommand();
                return publishCurrentWorkspaceCmd;
            }
        }

        private static PublishSelectedNodeCommand publishSelectedNodeCmd;
        public static PublishSelectedNodeCommand PublishSelectedNodeCmd
        {
            get
            {
                if (publishSelectedNodeCmd == null)
                    publishSelectedNodeCmd = new PublishSelectedNodeCommand();
                return publishSelectedNodeCmd;
            }
        }

        private static RefreshRemotePackagesCommand refreshRemotePackagesCmd;
        public static RefreshRemotePackagesCommand RefreshRemotePackagesCmd
        {
            get
            {
                if (refreshRemotePackagesCmd == null)
                    refreshRemotePackagesCmd = new RefreshRemotePackagesCommand();
                return refreshRemotePackagesCmd;
            }
        }

        private static ShowLoginCommand showLoginCmd;
        public static ShowLoginCommand ShowLoginCmd
        {
            get
            {
                if (showLoginCmd == null)
                    showLoginCmd = new ShowLoginCommand();
                return showLoginCmd;
            }
        }

        private static LoginCommand loginCmd;
        public static LoginCommand LoginCmd
        {
            get
            {
                if (loginCmd == null)
                    loginCmd = new LoginCommand();
                return loginCmd;
            }
        }
    }
}
