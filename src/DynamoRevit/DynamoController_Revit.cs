﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Reflection;
using System.IO;
using System.Diagnostics;

using Dynamo.Nodes;
using Dynamo.Revit;
using Dynamo.Controls;
using Dynamo.Utilities;

using Autodesk.Revit.DB;

using Value = Dynamo.FScheme.Value;


namespace Dynamo
{
    public class DynamoController_Revit : DynamoController
    {
        public DynamoUpdater Updater { get; private set; }

        PredicateTraverser checkManualTransaction;
        PredicateTraverser checkRequiresTransaction;

        dynamic oldPyEval;

        public DynamoController_Revit(FSchemeInterop.ExecutionEnvironment env, DynamoUpdater updater)
            : base(env)
        {
            Updater = updater;
            
            dynRevitSettings.Controller = this;

            Predicate<dynNode> manualTransactionPredicate = delegate(dynNode node)
            {
                return node is dynTransaction;
            };
            checkManualTransaction = new PredicateTraverser(manualTransactionPredicate);

            Predicate<dynNode> requiresTransactionPredicate = delegate(dynNode node)
            {
                return node is dynRevitTransactionNode;
            };
            checkRequiresTransaction = new PredicateTraverser(requiresTransactionPredicate);

            AddPythonBindings();
            AddWatchNodeHandler();

            dynRevitSettings.Revit.Application.DocumentClosed += new EventHandler<Autodesk.Revit.DB.Events.DocumentClosedEventArgs>(Application_DocumentClosed);
            dynRevitSettings.Revit.Application.DocumentOpened += new EventHandler<Autodesk.Revit.DB.Events.DocumentOpenedEventArgs>(Application_DocumentOpened);
        }

        void Application_DocumentOpened(object sender, Autodesk.Revit.DB.Events.DocumentOpenedEventArgs e)
        {
            //when a document is closed
            if (dynRevitSettings.Doc == null)
            {
                dynRevitSettings.Doc = dynRevitSettings.Revit.ActiveUIDocument;
                Bench.Controller.RunEnabled = true;
            }
        }

        void Application_DocumentClosed(object sender, Autodesk.Revit.DB.Events.DocumentClosedEventArgs e)
        {
            //Disable running against revit without a document
            dynRevitSettings.Doc = null;
            Bench.Controller.RunEnabled = false;
        }

        #region Python Nodes Revit Hooks
        private delegate void LogDelegate(string msg);
        private delegate void SaveElementDelegate(Autodesk.Revit.DB.Element e);

        void AddPythonBindings()
        {
            try
            {
                var assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                Assembly ironPythonAssembly = null;

                string path;

                if (File.Exists(path = Path.Combine(assemblyPath, "DynamoPython.dll")))
                {
                    ironPythonAssembly = Assembly.LoadFrom(path);
                }
                else if (File.Exists(path = Path.Combine(assemblyPath, "Packages", "IronPython", "DynamoPython.dll")))
                {
                    ironPythonAssembly = Assembly.LoadFrom(path);
                }

                if (ironPythonAssembly == null)
                    throw new Exception();

                var PythonBindings = ironPythonAssembly.GetType("Dynamo.Nodes.PythonBindings");

                var pyBindingsProperty = PythonBindings.GetProperty("Bindings");
                var pyBindings = pyBindingsProperty.GetValue(null, null);

                var Binding = ironPythonAssembly.GetType("Dynamo.Nodes.Binding");

                Func<string, object, object> CreateBinding = delegate(string name, object boundObject)
                {
                    return Activator.CreateInstance(Binding, new object[] { name, boundObject });
                };

                Action<string, object> AddToBindings = delegate(string name, object boundObject)
                {
                    pyBindings.GetType().InvokeMember("Add", BindingFlags.InvokeMethod, null, pyBindings, new object[] { CreateBinding(name, boundObject) });
                };

                AddToBindings("DynLog", new LogDelegate(Bench.Log)); //Logging

                AddToBindings(
                   "DynTransaction",
                   new Func<Autodesk.Revit.DB.SubTransaction>(
                      delegate
                      {
                          if (!dynRevitSettings.Controller.IsTransactionActive())
                          {
                              dynRevitSettings.Controller.InitTransaction();
                          }
                          return new Autodesk.Revit.DB.SubTransaction(dynRevitSettings.Doc.Document);
                      }));

                AddToBindings("__revit__", dynRevitSettings.Doc.Application);
                AddToBindings("__doc__", dynRevitSettings.Doc.Application.ActiveUIDocument.Document);

                var PythonEngine = ironPythonAssembly.GetType("Dynamo.Nodes.PythonEngine");
                var evaluatorField = PythonEngine.GetField("Evaluator");

                oldPyEval = (dynamic)evaluatorField.GetValue(null);

                //var x = PythonEngine.GetMembers();
                //foreach (var y in x)
                //    Console.WriteLine(y);

                var evalDelegateType = ironPythonAssembly.GetType("Dynamo.Nodes.PythonEngine+EvaluationDelegate");

                Delegate d = Delegate.CreateDelegate(
                    evalDelegateType,
                    this,
                    typeof(DynamoController_Revit)
                        .GetMethod("newEval", BindingFlags.NonPublic | BindingFlags.Instance));

                evaluatorField.SetValue(
                    null,
                    d);

                // use this to pass into the python script a list of previously created elements from dynamo
                //TODO: ADD BACK IN
                //bindings.Add(new Binding("DynStoredElements", this.Elements));
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                Debug.WriteLine(e.StackTrace);
            }
        }

        Value newEval(bool dirty, string script, dynamic bindings)
        {
            bool transactionRunning = Transaction != null && Transaction.GetStatus() == TransactionStatus.Started;

            Value result = null;

            if (dynRevitSettings.Controller.InIdleThread)
                result = oldPyEval(dirty, script, bindings);
            else
            {
                result = IdlePromise<Value>.ExecuteOnIdle(
                   () => oldPyEval(dirty, script, bindings));
            }

            if (transactionRunning)
            {
                if (!IsTransactionActive())
                {
                    InitTransaction();
                }
                else
                {
                    var ts = Transaction.GetStatus();
                    if (ts != TransactionStatus.Started)
                    {
                        if (ts != TransactionStatus.RolledBack)
                            CancelTransaction();
                        InitTransaction();
                    }
                }
            }
            else if (RunInDebug)
            {
                if (IsTransactionActive())
                    EndTransaction();
            }

            return result;
        }
        #endregion

        #region Watch Node Revit Hooks
        void AddWatchNodeHandler()
        {
            dynWatch.AddWatchHandler(new RevitElementWatchHandler());
        }

        private class RevitElementWatchHandler : WatchHandler
        {
            #region WatchHandler Members

            public bool AcceptsValue(object o)
            {
                return o is Element;
            }

            public void ProcessNode(object value, WatchNode node)
            {
                var element = value as Element;
                var id = element.Id;

                node.Clicked += delegate
                {
                    dynRevitSettings.Doc.ShowElements(element);
                };

                node.Link = id.ToString();
            }

            #endregion
        }
        #endregion

        protected override dynFunction CreateFunction(IEnumerable<string> inputs, IEnumerable<string> outputs, FunctionDefinition functionDefinition)
        {
            if (functionDefinition.Workspace.Nodes.Any(x => x is dynRevitTransactionNode)
                || functionDefinition.Dependencies.Any(d => d.Workspace.Nodes.Any(x => x is dynRevitTransactionNode)))
            {
                return new dynFunctionWithRevit(inputs, outputs, functionDefinition);
            }
            return base.CreateFunction(inputs, outputs, functionDefinition);
        }

        public bool InIdleThread;

        public enum TransactionMode
        {
            Automatic,
            Manual,
            Debug
        }

        private TransactionMode transMode;
        public TransactionMode TransMode
        {
            get { return transMode; }
            set
            {
                transMode = value;
                if (transMode == TransactionMode.Debug)
                {
                    this.RunInDebug = true;
                }

                NotifyPropertyChanged("TransMode");
            }
        }

        public override bool CanRunDynamically
        {
            get
            {
                //we don't want to be able to run
                //dynamically if we're in debug mode
                bool manTran = ExecutionRequiresManualTransaction();
                return !manTran && !debug;
            }
            set
            {
                canRunDynamically = value;
                NotifyPropertyChanged("CanRunDynamically");
            }
        }

        public override bool DynamicRunEnabled
        {
            get
            {
                return dynamicRun;
            }
            set
            {
                dynamicRun = value;
                NotifyPropertyChanged("DynamicRunEnabled");
            }
        }

        public override bool RunInDebug
        {
            get { return debug; }
            set
            {
                debug = value;

                //toggle off dynamic run
                CanRunDynamically = !debug;

                if(debug == true)
                    DynamicRunEnabled = false;

                NotifyPropertyChanged("RunInDebug");
            }
        }

        bool ExecutionRequiresManualTransaction()
        {
            //if there are no topmost nodes, just return false
            //this will avoid a binding error during bench initialization
            if (HomeSpace.GetTopMostNodes().Count() > 0)
            {
                return HomeSpace.GetTopMostNodes().Any(
                    checkManualTransaction.TraverseUntilAny
                );
            }
            else
            {
                return false;
            }
        }

        private List<Autodesk.Revit.DB.ElementId> _transElements = new List<Autodesk.Revit.DB.ElementId>();

        private Dictionary<Autodesk.Revit.DB.ElementId, DynElementUpdateDelegate> _transDelElements
           = new Dictionary<Autodesk.Revit.DB.ElementId, DynElementUpdateDelegate>();

        internal void RegisterSuccessfulDeleteHook(Autodesk.Revit.DB.ElementId id, DynElementUpdateDelegate d)
        {
            this._transDelElements[id] = d;
        }

        private void CommitDeletions()
        {
            var delDict = new Dictionary<DynElementUpdateDelegate, List<Autodesk.Revit.DB.ElementId>>();
            foreach (var kvp in this._transDelElements)
            {
                if (!delDict.ContainsKey(kvp.Value))
                {
                    delDict[kvp.Value] = new List<Autodesk.Revit.DB.ElementId>();
                }
                delDict[kvp.Value].Add(kvp.Key);
            }

            foreach (var kvp in delDict)
                kvp.Key(kvp.Value);
        }

        internal void RegisterDeleteHook(Autodesk.Revit.DB.ElementId id, DynElementUpdateDelegate d)
        {
            DynElementUpdateDelegate del = delegate(List<Autodesk.Revit.DB.ElementId> deleted)
            {
                var valid = new List<Autodesk.Revit.DB.ElementId>();
                var invalid = new List<Autodesk.Revit.DB.ElementId>();
                foreach (var delId in deleted)
                {
                    try
                    {
                        Autodesk.Revit.DB.Element e = dynRevitSettings.Doc.Document.GetElement(delId);
                        if (e != null)
                        {
                            valid.Add(e.Id);
                        }
                        else
                            invalid.Add(delId);
                    }
                    catch
                    {
                        invalid.Add(delId);
                    }
                }
                valid.Clear();
                d(invalid);
                foreach (var invId in invalid)
                {
                    this.Updater.UnRegisterChangeHook(invId, ChangeTypeEnum.Modify);
                    this.Updater.UnRegisterChangeHook(invId, ChangeTypeEnum.Add);
                    this.Updater.UnRegisterChangeHook(invId, ChangeTypeEnum.Delete);
                }
            };

            DynElementUpdateDelegate mod = delegate(List<Autodesk.Revit.DB.ElementId> modded)
            {
                _transElements.RemoveAll(modded.Contains);

                foreach (var mid in modded)
                {
                    this.Updater.UnRegisterChangeHook(mid, ChangeTypeEnum.Modify);
                    this.Updater.UnRegisterChangeHook(mid, ChangeTypeEnum.Add);
                }
            };

            this.Updater.RegisterChangeHook(
               id, ChangeTypeEnum.Delete, del
            );
            this.Updater.RegisterChangeHook(
               id, ChangeTypeEnum.Modify, mod
            );
            this.Updater.RegisterChangeHook(
               id, ChangeTypeEnum.Add, mod
            );
            this._transElements.Add(id);
        }

        private Transaction _trans;
        public void InitTransaction()
        {
            if (_trans == null || _trans.GetStatus() != TransactionStatus.Started)
            {
                _trans = new Transaction(
                   dynRevitSettings.Doc.Document,
                   "Dynamo Script"
                );
                _trans.Start();

                FailureHandlingOptions failOpt = _trans.GetFailureHandlingOptions();
                failOpt.SetFailuresPreprocessor(new DynamoWarningPrinter(this.Bench));
                _trans.SetFailureHandlingOptions(failOpt);
            }
        }

        public Transaction Transaction { get { return this._trans; } }

        public void EndTransaction()
        {
            if (_trans != null)
            {
                if (_trans.GetStatus() == TransactionStatus.Started)
                {
                    _trans.Commit();
                    _transElements.Clear();
                    CommitDeletions();
                    _transDelElements.Clear();
                }
                _trans = null;
            }
        }

        public void CancelTransaction()
        {
            if (_trans != null)
            {
                _trans.RollBack();
                _trans = null;
                this.Updater.RollBack(this._transElements);
                this._transElements.Clear();
                this._transDelElements.Clear();
            }
        }

        public bool IsTransactionActive()
        {
            return _trans != null;
        }

        protected override void OnRunCancelled(bool error)
        {
            base.OnRunCancelled(error);

            this.CancelTransaction();
        }

        protected override void OnEvaluationCompleted()
        {
            base.OnEvaluationCompleted();

            //Cleanup Delegate
            Action cleanup = delegate
            {
                this.InitTransaction(); //Initialize a transaction (if one hasn't been aleady)

                //Reset all elements
                foreach (var element in this.AllNodes)
                {
                    if (element is dynRevitTransactionNode)
                        (element as dynRevitTransactionNode).ResetRuns();
                }

                //////
                /* FOR NON-DEBUG RUNS, THIS IS THE ACTUAL END POINT FOR DYNAMO TRANSACTION */
                //////

                this.EndTransaction(); //Close global transaction.
            };

            //If we're in a debug run or not already in the idle thread, then run the Cleanup Delegate
            //from the idle thread. Otherwise, just run it in this thread.
            if (RunInDebug || !InIdleThread)
                IdlePromise.ExecuteOnIdle(cleanup, false);
            else
                cleanup();
        }

        protected override void Run(IEnumerable<dynNode> topElements, FScheme.Expression runningExpression)
        {

            //If we are not running in debug...
            if (!this.RunInDebug)
            {
                //Do we need manual transaction control?
                bool manualTrans = topElements.Any(checkManualTransaction.TraverseUntilAny);

                //Can we avoid running everything in the Revit Idle thread?
                bool noIdleThread = manualTrans || !topElements.Any(checkRequiresTransaction.TraverseUntilAny);

                //If we don't need to be in the idle thread...
                if (noIdleThread)
                {
                    this.TransMode = TransactionMode.Manual; //Manual transaction control
                    this.InIdleThread = false; //Not in idle thread at the moment
                    base.Run(topElements, runningExpression); //Just run the Run Delegate
                }
                else //otherwise...
                {
                    this.TransMode = TransactionMode.Automatic; //Automatic transaction control
                    this.InIdleThread = true; //Now in the idle thread.
                    IdlePromise.ExecuteOnIdle(new Action(
                        () => base.Run(topElements, runningExpression)),
                        false); //Execute the Run Delegate in the Idle thread.
                }
            }
            else //If we are in debug mode...
            {
                this.TransMode = TransactionMode.Debug; //Debug transaction control
                this.InIdleThread = true; //Everything will be evaluated in the idle thread.

                Bench.Dispatcher.Invoke(new Action(
                   () => Bench.Log("Running expression in debug.")
                ));

                //Execute the Run Delegate.
                base.Run(topElements, runningExpression);
            }
        }
    }

    public class DynamoWarningPrinter : Autodesk.Revit.DB.IFailuresPreprocessor
    {
        dynBench bench;

        public DynamoWarningPrinter(dynBench b)
        {
            this.bench = b;
        }

        public Autodesk.Revit.DB.FailureProcessingResult PreprocessFailures(Autodesk.Revit.DB.FailuresAccessor failuresAccessor)
        {
            var failList = failuresAccessor.GetFailureMessages();
            foreach (var fail in failList)
            {
                var severity = fail.GetSeverity();
                if (severity == Autodesk.Revit.DB.FailureSeverity.Warning)
                {
                    bench.Log(
                       "!! Warning: " + fail.GetDescriptionText()
                    );
                    failuresAccessor.DeleteWarning(fail);
                }
            }

            return Autodesk.Revit.DB.FailureProcessingResult.Continue;
        }
    }
}
