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
    "You are observing a T-shaped conveyor-based sorting system in a factory. "

    "## System Components "
        "1. Horizontal Conveyor (Right Side) "
            "- Begins at the far right end. "
            "- Ends at the circular boundary of the turntable. "

        "2. Turntable (Center) "
            "- A circular rotating platform that connects the horizontal and vertical conveyors. "
            "- The outer boundary is defined by a pale ring encasing the rotating platform. "
            "- Two side/guide rails are mounted on the turntable surface to secure item alignment during rotation. Their orientation indicates whether the turntable is rotated (horizontal = unrotated; vertical = rotated 90°). "

        "3. Vertical Conveyor (Left Side) "
            "- Extends vertically and connects to the turntable. "
            "- Composed of a top section and bottom section. "

    "## Frame Analysis Instructions "
        "For each individual frame: "
            "- Treat each frame completely individual. "
            "- Describe exactly where the blue item with a wooden pallet underneath is, using clear and precise terms such as: "
                "- \"At right end of horizontal conveyor\" "
                "- \"Partially on turntable\" "
                "- \"Fully on turntable\" "
                "- \"At top/bottom of vertical conveyor\" "
                "- \"Misaligned\" or \"tilted\" (if applicable) "

    "## Key Observational Priorities "
        "- If the item is fully or partially on the turntable, decide whether the turntable is rotated: "
            "- Check for turntable guide rail direction. "
        "- Assess whether the item is partially or fully on a given component using boundary rules. "
        "- Watch for any tilt or misalignment, especially at transfer points (e.g., turntable entrance/exit). "
        "- Confirm whether the item reaches the bottom of the vertical conveyor — the final correct position. "
)
print('prompt text ready')

log_file = 'output_query_single_frame.log'

with open(log_file, 'a', encoding='utf-8') as f:
    for frame_folder in os.listdir('sampled_frames'):
        print(frame_folder)
        f.write(f"=== Manipulation: {frame_folder} ===\n\n")

        # Messages containing a local video path and a text query
        messages = [
            {
                "role": "user",
                "content": [
                    {"type": "image", "image": f"sampled_frames/{frame_folder}/frame_5.jpg"},
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