from transformers import Qwen2_5_VLForConditionalGeneration, AutoTokenizer, AutoProcessor
from qwen_vl_utils import process_vision_info
import os
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

base_prompt = (
    "You are observing a T-shape conveyor-based sorting system in a factory setting. "
    "The system has the following components: "
        "1. A horizontal conveyor on the right side, where the process begins. "
        "2. A turntable located at the center left. "
        "3. A vertical conveyor on the left, split into two sections: one above and one below the turntable. "
    "The intended movement of the {complete_item} is as follows: "
        "1. The {item} moves from right to left along the horizontal conveyor. "
        "2. The {item} is loaded onto the turntable."
        "3. The turntable rotates and unloads the {item} onto the vertical conveyor. "
        "4.The {item} moves {direction} along the vertical conveyor. "
        "5. The {item} should reach the final destination at {destination} end of the vertical conveyor. "
    "Task: Carefully track the {item} throughout the video. Determine whether it reaches the correct final destination. "
    "Think step by step while analyzing the sequence. Base your reasoning only on the actual behavior of the {item} in the video."
)
prompt_text_dict = {
    "blue_square": base_prompt.format(complete_item="blue item with a wooden pallet under it", item="blue item", direction="downwards", destination="bottom"),
    "green_square": base_prompt.format(complete_item="green item with a wooden pallet under it", item="green item", direction="downwards", destination="bottom"),
    "pallet": base_prompt.format(complete_item="wooden pallet", item="pallet", direction="downwards", destination="bottom"),
    "white_box": base_prompt.format(complete_item="white box with a wooden pallet under it", item="white box", direction="upwards", destination="top"),
    "yellow_box": base_prompt.format(complete_item="yellow box with a wooden pallet under it", item="yellow box", direction="downwards", destination="bottom")
}
print('prompt text ready')

log_file = 'output_query_video.log'

with open(log_file, 'a', encoding='utf-8') as f:
    for video_folder in os.listdir('videos'):
        print(video_folder)
        video_path = 'videos/' + video_folder + '/'

        f.write(f"=== Item: {video_folder} ===\n\n")
        
        prompt_text = prompt_text_dict[video_folder]

        for video_file in os.listdir(video_path):
            print(video_file)
            messages = [
                {
                    "role": "user",
                    "content": [
                        {
                            "type": "video",
                            "video": video_path + video_file,
                            "max_pixels": 1420 * 880,
                            "fps": 1,
                        },
                        {"type": "text", "text": prompt_text}
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
            inputs = inputs.to(model.device)

            # Inference: Generation of the output
            generated_ids = model.generate(**inputs, max_new_tokens=512)
            generated_ids_trimmed = [
                out_ids[len(in_ids) :] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)
            ]
            output_text = processor.batch_decode(
                generated_ids_trimmed, skip_special_tokens=True, clean_up_tokenization_spaces=False
            )

            f.write(f"=== Manipulation: {video_file.split('.')[0]} ===\n")
            for line in output_text:
                f.write(line + "\n")
            f.write('\n')