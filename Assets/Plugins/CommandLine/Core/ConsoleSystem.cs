using System;
using System.Collections.Generic;
using UnityEngine;

namespace RedSaw.CommandLineInterface{

    /// <summary>use to record command input history</summary>
    class InputHistory{

        readonly List<string> history;
        readonly int capacity;
        int lastIndex;

        public InputHistory(int capacity){

            this.lastIndex = 0;
            this.capacity = capacity;
            this.history = new List<string>();
        }
        /// <summary>record input string</summary>
        public void Record(string input){
            
            if(history.Count == capacity){
                history.RemoveAt(0);
            }
            history.Add(input);
            lastIndex = history.Count - 1;
        }

        /// <summary>get last input string</summary>
        public string Last{
            get{
                if(history.Count == 0)return string.Empty;
                if(lastIndex < 0 || lastIndex >= history.Count)lastIndex = history.Count - 1;
                return history[lastIndex --];
            }
        }

        /// <summary>get next input string</summary>
        public string Next{
            get{
                if(history.Count == 0)return string.Empty;
                if(lastIndex >= history.Count || lastIndex < 0)lastIndex = 0;
                return history[lastIndex ++];
            }
        }
    }


    /// <summary>command selector</summary>
    class LinearSelector{

        public event Action<int> OnSelectionChanged;
        readonly List<string> optionsBuffer;
        int currentIndex;

        int TotalCount{
            get{
                if(optionsBuffer == null)return 0;
                return optionsBuffer.Count;
            }
        }

        /// <summary>
        /// the current selection of the alternative options
        /// if value is -1, it means there's no alternative options choosed
        /// </summary>
        public int SelectionIndex => currentIndex;

        public LinearSelector(){

            this.optionsBuffer = new();
            this.currentIndex = -1;
        }

        public void LoadOptions(List<string> options){

            this.optionsBuffer.Clear();
            this.optionsBuffer.AddRange(options);
            this.currentIndex = -1;
        }

        public bool GetCurrentSelection(out string selection){

            selection = string.Empty;
            if(currentIndex == -1)return false;
            selection = optionsBuffer[currentIndex];
            return true;
        }

        /// <summary>move to next alternative option</summary>
        public void MoveNext(){

            if(TotalCount == 0)return;
            if(currentIndex == -1){
                currentIndex = 0;
                OnSelectionChanged?.Invoke(currentIndex);
                return;
            }
            currentIndex = currentIndex < TotalCount - 1 ? currentIndex + 1 : 0;
            OnSelectionChanged?.Invoke(currentIndex);
        }

        /// <summary>move to last alternative option</summary>
        public void MoveLast(){

            if(TotalCount == 0)return;
            if(currentIndex == -1){
                currentIndex = TotalCount - 1;
                OnSelectionChanged?.Invoke(currentIndex);
                return;
            }
            currentIndex = currentIndex > 0 ? currentIndex - 1 : TotalCount - 1;
            OnSelectionChanged?.Invoke(currentIndex);
        }
    }

    /// <summary>command console</summary>
    public class Console{

        class CmdImpl : ICommandSystem
        {
            public IEnumerable<string> CommandInfos{
                get{
                    foreach(var cmd in CommandSystem.TotalCommands){
                        yield return $"{cmd.name}: {cmd.description}";
                    }
                }
            }
            public bool Execute(string command) => CommandSystem.Execute(command);
            public bool ExecuteSlience(string command) => CommandSystem.ExecuteSlience(command);

            public void SetOutputFunc(Action<string> outputFunc) => CommandSystem.SetOutputFunc(outputFunc);
            public void SetOutputErrFunc(Action<string> outputFunc) => CommandSystem.SetOutputErrFunc(outputFunc);
            public List<(string, string)> Query(string input, int count)
            {
                var commands = CommandSystem.QueryCommands(input, count, CLIUtils.SimpleFilter);
                var result = new List<(string, string)>();
                foreach(var command in commands){
                    result.Add((command.name, command.description));
                }
                return result;
            }
            public void UseDefualtCommand() => CommandSystem.CollectDefaultCommand();
        }


        public event Action OnFocusOut;
        public event Action OnFocus;

        readonly int alternativeCommandCount;
        readonly bool shouldRecordFailedCommand;
        readonly bool outputWithTime;

        readonly ICommandSystem commandSystem;
        readonly IConsoleRenderer renderer;
        readonly IConsoleInput userInput;

        readonly InputHistory inputHistory;
        readonly LinearSelector selector;
        bool throwTextChanged = false;

        public string TimeInfo{
            get{
                if(outputWithTime){
                    var time = DateTime.Now;
                    return $"[{time.Hour}:{time.Minute}:{time.Second}] ";
                }
                return string.Empty;
            }
        }

        public IEnumerable<string> TotalCommandInfos => commandSystem.CommandInfos;
        public ICommandSystem CurrentCommandSystem => commandSystem;

        public Console(
            IConsoleRenderer renderer, 
            IConsoleInput userInput,
            ICommandSystem commandSystem = null,
            int memoryCapacity = 20,
            int alternativeCommandCount = 8,
            bool shouldRecordFailedCommand = true,
            int outputPanelCapacity = 400,
            bool outputWithTime = true,
            bool useDefaultCommand = true
        ){
            // about renderer
            this.renderer = renderer;
            renderer.OutputPanelCapacity = Mathf.Max(outputPanelCapacity, 100);
            renderer.BindOnSubmit(OnSubmit);
            renderer.BindOnTextChanged(OnTextChanged);

            // about command system
            this.commandSystem = commandSystem ?? new CmdImpl();
            this.commandSystem.SetOutputFunc(s => Output(s));
            this.commandSystem.SetOutputErrFunc(s => Output(s, "#ff0000"));
            if(useDefaultCommand)this.commandSystem.UseDefualtCommand();

            // other things
            this.userInput = userInput;
            inputHistory = new InputHistory(Math.Max(memoryCapacity, 2));
            
            selector = new LinearSelector();
            this.selector.OnSelectionChanged += idx => {
                renderer.AlternativeOptionsIndex = idx;
                renderer.MoveCursorToEnd();
            };
            
            this.alternativeCommandCount = Math.Max(alternativeCommandCount, 1);
            this.shouldRecordFailedCommand = shouldRecordFailedCommand;
            this.outputWithTime = outputWithTime;
        }
        public void Update(){

            if(userInput.ShowOrHide){
                renderer.IsVisible = !renderer.IsVisible;
            }
            if(!renderer.IsVisible)return;

            if(renderer.IsInputFieldFocus){

                // quit focus
                if(userInput.QuitFocus){

                    renderer.QuitFocus();
                    OnFocusOut?.Invoke();
                }
                else if(userInput.MoveDown){

                    if(renderer.IsAlternativeOptionsActive){
                        selector.MoveNext();
                    }else{
                        throwTextChanged = true;
                        renderer.InputText = inputHistory.Next;
                        renderer.MoveCursorToEnd();
                    }
                }else if(userInput.MoveUp){

                    if(renderer.IsAlternativeOptionsActive){
                        selector.MoveLast();
                    }else{
                        throwTextChanged = true;
                        renderer.InputText = inputHistory.Last;
                        renderer.MoveCursorToEnd();
                    }
                }
                return;
            }
            if(userInput.Focus){
                renderer.Focus();
                renderer.ActivateInput();
                OnFocus?.Invoke();
            }
        }
        
        /// <summary>Output message on console</summary>
        public void Output(string msg) => renderer.Output(TimeInfo + msg);

        /// <summary>Output message on console with given color</summary>
        public void Output(string msg, string color = "#ff0000") => renderer.Output(TimeInfo + msg, color);

        /// <summary>Output message on console</summary>
        public void Output(string[] msg, string color = "#ffffff"){

            if(outputWithTime){
                var timeInfo = TimeInfo;
                for(int i = 0; i < msg.Length; i ++){
                    msg[i] = timeInfo + msg[i];
                }
                renderer.Output(msg, color);
                return;
            }
            renderer.Output(msg, color);
        }

        /// <summary>Output message on console</summary>
        public void Output(string[] msg){

            if(outputWithTime){
                var timeInfo = TimeInfo;
                for(int i = 0; i < msg.Length; i ++){
                    msg[i] = timeInfo + msg[i];
                }
                renderer.Output(msg);
                return;
            }
            renderer.Output(msg);
        }

        /// <summary>clear current console</summary>
        public void ClearOutputPanel() => renderer.Clear();

        public void OnTextChanged(string text){
            /* when input new string, should query commands from commandSystem */

            if(throwTextChanged){
                throwTextChanged = false;
                return;
            }

            /* hide options panel when there's no text or no commands found */
            if(text == null || text.Length == 0 || text.Contains(' ')){
                if(renderer.IsAlternativeOptionsActive){
                    renderer.IsAlternativeOptionsActive = false;
                }
                return;
            }

            var result = commandSystem.Query(text, alternativeCommandCount);
            if(result.Count == 0){
                if(renderer.IsAlternativeOptionsActive){
                    renderer.IsAlternativeOptionsActive = false;
                }
                return;
            }

            /* show options panel */
            var optionsBuffer = new List<string>();
            var list = new List<string>();
            foreach(var elem in result){
                optionsBuffer.Add(elem.Item1);
                list.Add($"{elem.Item1}: {elem.Item2}");
            }
            if(!renderer.IsAlternativeOptionsActive){
                renderer.IsAlternativeOptionsActive = true;
            }
            selector.LoadOptions(optionsBuffer);
            renderer.AlternativeOptions = list;
            renderer.AlternativeOptionsIndex = selector.SelectionIndex;
        }


        /// <summary>input string into current console</summary>
        public void OnSubmit(string text){

            if(renderer.IsAlternativeOptionsActive && selector.GetCurrentSelection(out string selection)){
                renderer.InputText = selection;
                renderer.IsAlternativeOptionsActive = false;
                renderer.ActivateInput();
                renderer.SetInputCursorPosition(renderer.InputText.Length);
                return;
            }

            if(renderer.IsAlternativeOptionsActive)renderer.IsAlternativeOptionsActive = false;

            Output(text);
            if(text.Length > 0){
                if(commandSystem.Execute(text)){
                    inputHistory.Record(text);
                }else{
                    if(shouldRecordFailedCommand)inputHistory.Record(text);
                }
                renderer.ActivateInput();
                renderer.InputText = string.Empty;
                renderer.MoveScrollBarToEnd();
            }
        }
    }
}