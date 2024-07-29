using System.Runtime.InteropServices;
[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]
namespace rkllm_sharp
{
    
    unsafe partial class _LLmInternal
    {
        [LibraryImport("librkllmrt.so")]
        protected static partial RKLLMParam rkllm_createDefaultParam();

        protected delegate void LLMResultCallback(RKLLMResult* result, IntPtr userdata, LLMCallState state);

        [LibraryImport("librkllmrt.so")]
        protected static partial int rkllm_init(out UIntPtr handle, RKLLMParam param, LLMResultCallback callback);

        [LibraryImport("librkllmrt.so")]
        protected static partial int rkllm_destroy(UIntPtr handle);

        [LibraryImport("librkllmrt.so")]
        protected static partial int rkllm_run(UIntPtr handle, IntPtr prompt, UIntPtr userdata);

        [LibraryImport("librkllmrt.so")]
        protected static partial int rkllm_abort(UIntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        protected struct RKLLMParam
        {
            public IntPtr model_path;     /* Path where the model file is located. */
            public int num_npu_core;       /* Number of NPU cores used for model inference. */
            public int max_context_len;    /* Maximum size of the context. */
            public int max_new_tokens;     /* Maximum number of tokens to generate during model inference. */
            public int top_k;              /* The number of highest probability tokens to consider for generation. */
            public float top_p;                /* Nucleus sampling: cumulative probability cutoff to use for token selection. */
            public float temperature;          /* Hyperparameter to control the randomness of predictions by scaling the logits before applying softmax. */
            public float repeat_penalty;       /* Penalty applied to the logits of previously generated tokens, helps prevent repetitive or monotonic text. */
            public float frequency_penalty;    /* Penalty for repeating the same word or phrase, reducing the likelihood of repeated content. */
            public float presence_penalty;     /* Penalty or reward for introducing new tokens into the generated text. */
            public int mirostat;           /* Enables mirostat algorithm, where 0 = off, 1 = use mirostat algorithm, 2 = use mirostat 2.0 algorithm. */
            public float mirostat_tau;         /* Target entropy (perplexity) for mirostat algorithm, setting the desired complexity of the generated text. */
            public float mirostat_eta;         /* Learning rate for the mirostat algorithm. */
            [MarshalAs(UnmanagedType.I1)]
            public bool logprobs;              /* Whether to return the log probabilities for each output token along with their token ids. */
            public int top_logprobs;       /* The number of top tokens for which to return log probabilities, along with their token ids. */
            [MarshalAs(UnmanagedType.I1)]
            public bool use_gpu;               /* Flag to indicate whether to use GPU for inference. */
        };

        [StructLayout(LayoutKind.Sequential)]
        protected struct RKLLMResult
        {
            public IntPtr text;           /* Decoded text from the inference output. */
            public IntPtr tokens;              /* Array of Token structures, each containing a log probability and a token ID. */
            int num;                    /* Number of top tokens returned, typically those with the highest probabilities. */
        };

        protected enum LLMCallState
        {
            LLM_RUN_NORMAL = 0,         /* Inference status is normal and inference has not yet finished. */
            LLM_RUN_FINISH = 1,         /* Inference status is normal and inference has finished. */
            LLM_RUN_ERROR = 2           /* Inference status is abnormal. */
        };
    }

    class Rkllm : _LLmInternal, IDisposable
    {
        private UIntPtr _handle = 0;
        private RKLLMParam _internalParam;
        private IntPtr _ptrPath = IntPtr.Zero;
        private IntPtr _ptrPrompt = IntPtr.Zero;
        private string _lastMessages = "";
        private string _lastFinalMessage = "";
        private bool disposed = false;
        private AutoResetEvent _messageFinish;
        public bool isRunning;
        public delegate void OnModelMessageHandler(string msg, ModelState state);
        public delegate void OnModelMessageFinalHandler(string msg, ModelState state);
        public event OnModelMessageHandler? OnModelMessage;
        public event OnModelMessageFinalHandler? OnModelFinalMessage;
        public Rkllm(string modelPath, RkllmParameters p)
        {
            FileInfo modelfile = new FileInfo(modelPath);
            if (!modelfile.Exists) throw new FileNotFoundException("Model file not found.");
            _ptrPath = Marshal.StringToHGlobalAnsi(modelfile.FullName);
            _internalParam.model_path = _ptrPath;
            _internalParam.num_npu_core = p.NumNpuCore;
            _internalParam.max_context_len = p.MaxContextLength;
            _internalParam.max_new_tokens = p.MaxNewTokens;
            _internalParam.top_k = p.TopK;
            _internalParam.top_p = p.TopP;
            _internalParam.temperature = p.Temperature;
            _internalParam.repeat_penalty = p.RepeatPenalty;
            _internalParam.frequency_penalty = p.FrequencyPenalty;
            _internalParam.presence_penalty = p.PresencePenalty;
            _internalParam.mirostat = p.MiroStatus;
            _internalParam.mirostat_tau = p.MiroStatusTargetEntropy;
            _internalParam.mirostat_eta = p.MiroStatusLearningRate;
            _internalParam.logprobs = p.isReturnLogProbabilities;
            _internalParam.top_logprobs = p.LogProbabilitiesCount;
            _internalParam.use_gpu = p.isUseGPU;
            _messageFinish = new AutoResetEvent(false);
            unsafe
            {
                var ret = rkllm_init(out _handle, _internalParam, callback);
                if (ret != 0)
                {
                    throw new Exception("RKllm init ERROR!!");
                }
            }
        }
        private unsafe void callback(RKLLMResult* _result, IntPtr userdata, LLMCallState _state)
        {
            ModelState state = (ModelState)(int)_state;
            if (state == ModelState.LLM_RUN_NORMAL)
            {
                isRunning = true;
                var str = Marshal.PtrToStringAnsi(_result->text);
                if (str == null) str = "";
                _lastMessages += str;
                if (OnModelMessage != null)
                    OnModelMessage(str, state);
            }
            if (state == ModelState.LLM_RUN_FINISH)
            {
                isRunning = false;
                if (OnModelFinalMessage != null)
                    OnModelFinalMessage(_lastMessages, state);

                _lastFinalMessage = _lastMessages;
                _messageFinish.Set();
                _lastMessages = "";
            }
            if (state == ModelState.LLM_RUN_ERROR)
            {
                isRunning = false;
                this.Dispose();
                throw new Exception("Text Genaration Error");
            }
        }

        public void Trigger(string prompt)
        {
            if (isRunning)
                throw new Exception("Cannot fire new prompt while the model is running!");
            if (_ptrPrompt != IntPtr.Zero)
                Marshal.FreeHGlobal(_ptrPrompt);
            _ptrPrompt = Marshal.StringToHGlobalAnsi(prompt);
            var ret = rkllm_run(_handle, _ptrPrompt, UIntPtr.Zero);
            if (ret != 0)
            {
                throw new Exception("RKLLM run Error");
            }
        }

        public async Task<string> RunAsync(string prompt)
        {
            if (isRunning)
                throw new Exception("Cannot fire new prompt while the model is running!");
            if (_ptrPrompt != IntPtr.Zero)
                Marshal.FreeHGlobal(_ptrPrompt);
            _ptrPrompt = Marshal.StringToHGlobalAnsi(prompt);
            var ret = rkllm_run(_handle, _ptrPrompt, UIntPtr.Zero);
            if (ret != 0)
            {
                throw new Exception("RKLLM run Error");
            }
            await Task.Run(()=> _messageFinish.WaitOne());
            var predict = _lastFinalMessage;

            return predict;
        }

        public void Abort()
        {
            isRunning = false;
            if (_ptrPrompt != IntPtr.Zero)
                Marshal.FreeHGlobal(_ptrPrompt);
            rkllm_abort(_handle);
        }
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {

                if (disposing)
                {
                    //other managed
                }
                rkllm_destroy(_handle);
                Marshal.FreeHGlobal(_ptrPath);
                Marshal.FreeHGlobal(_ptrPrompt);
                _handle = UIntPtr.Zero;

                // Note disposing has been done.
                disposed = true;
            }
        }

        ~Rkllm()
        {
            Dispose(disposing: false);
        }
        public enum ModelState
        {
            LLM_RUN_NORMAL = 0,         /* Inference status is normal and inference has not yet finished. */
            LLM_RUN_FINISH = 1,         /* Inference status is normal and inference has finished. */
            LLM_RUN_ERROR = 2           /* Inference status is abnormal. */
        };
    }

    class RkllmParameters
    {
        public int NumNpuCore = 1;       /* Number of NPU cores used for model inference. */
        public int MaxContextLength = 512;    /* Maximum size of the context. */
        public int MaxNewTokens = -1;     /* Maximum number of tokens to generate during model inference. */
        public int TopK = 40;              /* The number of highest probability tokens to consider for generation. */
        public float TopP = 0.9f;                /* Nucleus sampling: cumulative probability cutoff to use for token selection. */
        public float Temperature = 0.8f;          /* Hyperparameter to control the randomness of predictions by scaling the logits before applying softmax. */
        public float RepeatPenalty = 1.1f;       /* Penalty applied to the logits of previously generated tokens, helps prevent repetitive or monotonic text. */
        public float FrequencyPenalty = 0;    /* Penalty for repeating the same word or phrase, reducing the likelihood of repeated content. */
        public float PresencePenalty = 0;     /* Penalty or reward for introducing new tokens into the generated text. */
        public int MiroStatus = 0;           /* Enables mirostat algorithm, where 0 = off, 1 = use mirostat algorithm, 2 = use mirostat 2.0 algorithm. */
        public float MiroStatusTargetEntropy = 5;         /* Target entropy (perplexity) for mirostat algorithm, setting the desired complexity of the generated text. */
        public float MiroStatusLearningRate = 0.1f;         /* Learning rate for the mirostat algorithm. */
        public bool isReturnLogProbabilities = false;              /* Whether to return the log probabilities for each output token along with their token ids. */
        public int LogProbabilitiesCount = 5;       /* The number of top tokens for which to return log probabilities, along with their token ids. */
        public bool isUseGPU = false;               /* Flag to indicate whether to use GPU for inference. */
    }
}