from transformers import Qwen2_5_VLForConditionalGeneration, AutoTokenizer, AutoProcessor
from qwen_vl_utils import process_vision_info
import os
import numpy as np
print('library imported')


model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
    "Qwen/Qwen2.5-VL-32B-Instruct",
    cache_dir='/storage/home/hcoda1/5/yxie405/p-szonouz6-0/hf_cache',
    torch_dtype="auto",
    device_map="auto"
)
print('model loaded')

processor = AutoProcessor.from_pretrained(
    "Qwen/Qwen2.5-VL-32B-Instruct",
    cache_dir='/storage/home/hcoda1/5/yxie405/p-szonouz6-0/hf_cache'
)
print('processor ready')

prompt_text = (
"You are observing a T-shaped conveyor-based sorting system in a factory. The system includes: "
"1. A horizontal conveyor on the right side, where each item starts. "
"2. A turntable at the center left. "
"3. A vertical conveyor on the left, which has a top and bottom section. "

"The white box with a wooden pallet underneath is intended to follow this target movement sequence: "
"1. It moves from right to left along the horizontal conveyor. "
"2. It is loaded onto the turntable. "
"3. The turntable rotates. "
"4. It is unloaded onto the vertical conveyor. "
"5. It moves upwards to the top end of the vertical conveyor (this is the final correct destination). "

" Your Task: Frame-by-Frame Analysis Rules "
" Please carefully analyze a sequence of uniformly sampled frames from a video. For each frame: "

"* Describe exactly where the box is (e.g., rightmost of horizontal conveyor, on the turntable, misaligned, at top of vertical conveyor). "
"* Only describe movement when it is visibly shown between frames. "
"* Do not assume any transition or motion unless it is clearly visible. "
"* Do not infer box behavior from previous sequences — treat each sequence as isolated. "

"Pay close attention to: "
" * Whether the turntable is rotated (identify based on changes in arrow/groove direction or box orientation). "
" * Box alignment and tilt, especially near transitions (turntable entry/exit, vertical conveyor handoff). "
" * Partial placements on the turntable or conveyors. "

" Confirm whether the box reaches the correct final destination (top of the vertical conveyor)."
)
print('prompt text ready')

log_file = 'output_query_frame.log'

user_define_frames = {
    1: [8],
    2: [0, 8],
    3: [0, 5, 8],
    5: [0, 2, 4, 6, 8]
}

with open(log_file, 'a', encoding='utf-8') as f:
    for frame_folder in os.listdir('sampled_frames'):
        print(frame_folder)
        f.write(f"=== Manipulation: {frame_folder} ===\n\n")

        # Messages containing a local video path and a text query
        messages = [
            {
                "role": "user",
                "content": [
                    *[
                        {"type": "image", "image": f"sampled_frames/{frame_folder}/frame_{i}.jpg"}
                        for i in user_define_frames[5]
                    ],
                    {"type": "text", "text": prompt_text},
                ],
            }
        ]

        # Preparation for inference
        text = processor.apply_chat_template(
            messages, tokenize=False, add_generation_prompt=True
        )
        image_inputs, video_inputs = process_vision_info(messages)
        inputs = processor(
            text=[text],
            images=image_inputs,
            videos=video_inputs,
            padding=True,
            return_tensors="pt",
        )
        inputs = inputs.to("cuda")

        # Inference: Generation of the output
        generated_ids = model.generate(**inputs, max_new_tokens=1000)
        generated_ids_trimmed = [
            out_ids[len(in_ids) :] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)
        ]
        output_text = processor.batch_decode(
            generated_ids_trimmed, skip_special_tokens=True, clean_up_tokenization_spaces=False
        )
        
        for line in output_text:
            f.write(line + "\n")
        f.write('\n')