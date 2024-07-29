## This is a C# wrapper for [rkllm-runtime-linux](https://github.com/airockchip/rknn-llm)

With this wrapper you can use converted large language models on RK series chip. E.g. RK3588.

Note you need to convert your model to .rkllm, and you will need the librkllmrt.so.

You can find the lib [here](https://github.com/airockchip/rknn-llm/tree/main/rkllm-runtime/runtime/Linux/librkllm_api/aarch64).

According to [this tutorial](https://docs.radxa.com/rock5/rock5itx/app-development/rkllm_install), you need the rkllm-driver >= v0.9.6

Check the version by:

`$ sudo cat /proc/rknpu/version`

## Sample

````C#
var param = new RkllmParameters(){
                NumNpuCore = 3,     //3 for 3588
                MaxNewTokens = 512,
                Temperature = 9999
            };
var llm = new Rkllm("/home/orangepi/ssd/rkllm/qwen.rkllm",param);

var prompt = "<|im_start|>system 你是一个人工智能助手。<|im_end|>\n<|im_start|>user你妈死了<|im_end|>\n<|im_start|>assistant\n";

// llm.OnModelMessage += (string msg, Rkllm.ModelState state)=>{
// Console.Write(msg);
// };

// llm.OnModelFinalMessage += (string msg, Rkllm.ModelState state)=>{
// Console.WriteLine();
// Console.WriteLine(msg);
// };

// llm.Trigger(prompt);

var resoponse = await llm.RunAsync(prompt);
Console.WriteLine(resoponse);
llm.Dispose();
````


