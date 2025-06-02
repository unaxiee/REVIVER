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
    "You are observing a T-shape conveyor-based sorting system in a factory setting."
    "The system has the following components:"
        "1. A horizontal conveyor on the right side, where the process begins."
        "2. A turntable located at the center left."
        "3. A vertical conveyor on the left, split into two sections: one above and one below the turntable."
    "The intended movement of the {complete_item} is as follows:"
        "1. The {item} moves from right to left along the horizontal conveyor."
        "2. The {item} is loaded onto the turntable."
        "3. The turntable rotates and unloads the {item} onto the vertical conveyor."
        "4.The {item} moves {direction} along the vertical conveyor."
        "5. The {item} should reach the final destination at {destination} end of the vertical conveyor."
    "Task: Carefully track the {item} throughout the video. Determine whether it reaches the correct final destination. Think step by step while analyzing the sequence. Base your reasoning only on the actual behavior of the {item} in the video."
)

prompt_text_dict = {
    "blue_square": base_prompt.format(complete_item="blue item with a wooden pallet under it", item="blue item", direction="downwards", destination="bottom"),
    "green_square": base_prompt.format(complete_item="green item with a wooden pallet under it", item="green item", direction="downwards", destination="bottom"),
    "pallet": base_prompt.format(complete_item="wooden pallet", item="pallet", direction="downwards", destination="bottom"),
    "white_box": base_prompt.format(complete_item="white box with a wooden pallet under it", item="white box", direction="upwnwards", destination="top"),
    "yellow_box": base_prompt(complete_item="yellow box with a wooden pallet under it", item="yellow box", direction="downwards", destination="bottom")
}

# prompt_text_dict = {
#     "blue_square": "You are observing a T-shape conveyor-based sorting system in a factory setting."
#                    "The system has the following components:"
#                         "1. A horizontal conveyor on the right side, where the process begins."
#                         "2. A turntable located at the center left."
#                         "3. A vertical conveyor on the left, split into two sections: one above and one below the turntable."
#                     "The intended movement of the blue item with a wooden pallet under it is as follows:"
#                         "1. The blue item moves from right to left along the horizontal conveyor."
#                         "2. The blue item is loaded onto the turntable."
#                         "3. The turntable rotates and unloads the blue item onto the vertical conveyor."
#                         "4.The blue item moves downwards along the vertical conveyor."
#                         "5. The blue item should reach the final destination at bottom end of the vertical conveyor."
#                     "Task: Carefully track the blue item throughout the video. Determine whether it reaches the correct final destination. Think step by step while analyzing the sequence. Base your reasoning only on the actual behavior of the blue item in the video.",
#     "green_square": "You are observing a T-shape conveyor-based sorting system in a factory setting."
#                     "The system has the following components:"
#                         "1. A horizontal conveyor on the right side, where the process begins."
#                         "2. A turntable located at the center left."
#                         "3. A vertical conveyor on the left, split into two sections: one above and one below the turntable."
#                     "The intended movement of the green item with a wooden pallet under it is as follows:"
#                         "1. The green item moves from right to left along the horizontal conveyor."
#                         "2. The green item is loaded onto the turntable."
#                         "3. The turntable rotates and unloads the green item onto the vertical conveyor."
#                         "4.The green item moves downwards along the vertical conveyor."
#                         "5. The green item should reach the final destination at the bottom end of the vertical conveyor."
#                     "Task: Carefully track the green item throughout the video. Determine whether it reaches the correct final destination. Think step by step while analyzing the sequence. Base your reasoning only on the actual behavior of the green item in the video.",
#     "pallet": "You are observing a T-shape conveyor-based sorting system in a factory setting."
#               "The system has the following components:"
#                     "1. A horizontal conveyor on the right side, where the process begins."
#                     "2. A turntable located at the center left."
#                     "3. A vertical conveyor on the left, split into two sections: one above and one below the turntable."
#               "The intended movement of the wooden pallet is as follows:"
#                     "1. The pallet moves from right to left along the horizontal conveyor."
#                     "2. The pallet is loaded onto the turntable."
#                     "3. The turntable rotates and unloads the pallet onto the vertical conveyor."
#                     "4.The pallet moves downwards along the vertical conveyor."
#                     "5. The pallet should reach the final destination at the bottom end of the vertical conveyor."
#               "Task: Carefully track the pallet throughout the video. Determine whether it reaches the correct final destination. Think step by step while analyzing the sequence. Base your reasoning only on the actual behavior of the pallet in the video.",
#     "white_box": "You are observing a T-shape conveyor-based sorting system in a factory setting."
#                  "The system has the following components:"
#                         "1. A horizontal conveyor on the right side, where the process begins."
#                         "2. A turntable located at the center left."
#                         "3. A vertical conveyor on the left, split into two sections: one above and one below the turntable."
#                  "The intended movement of the white box with a wooden pallet under it is as follows:"
#                         "1. The white box moves from right to left along the horizontal conveyor."
#                         "2. The white box is loaded onto the turntable."
#                         "3. The turntable rotates and unloads the white box onto the vertical conveyor."
#                         "4.The white box moves upwards along the vertical conveyor."
#                         "5. The white box should reach the final destination at the top end of the vertical conveyor."
#                  "Task: Carefully track the white box throughout the video. Determine whether it reaches the correct final destination. Think step by step while analyzing the sequence. Base your reasoning only on the actual behavior of the white box in the video.",
#     "yellow_box": "You are observing a T-shape conveyor-based sorting system in a factory setting."
#                   "The system has the following components:"
#                         "1. A horizontal conveyor on the right side, where the process begins."
#                         "2. A turntable located at the center left."
#                         "3. A vertical conveyor on the left, split into two sections: one above and one below the turntable."
#                   "The intended movement of the yellow box with a wooden pallet under it is as follows:"
#                         "1. The yellow box moves from right to left along the horizontal conveyor."
#                         "2. The yellow box is loaded onto the turntable."
#                         "3. The turntable rotates and unloads the yellow box onto the vertical conveyor."
#                         "4.The yellow box moves downwards along the vertical conveyor."
#                         "5. The yellow box should reach the final destination at the bottom end of the vertical conveyor."
#                  "Task: Carefully track the yellow box throughout the video. Determine whether it reaches the correct final destination. Think step by step while analyzing the sequence. Base your reasoning only on the actual behavior of the yellow box in the video.",
#     }
print('prompt text ready')

log_file = 'output_query_video.log'

for video_folder in os.listdir('videos'):
    print(video_folder)
    video_path = 'videos/' + video_folder + '/'
    with open(log_file, 'a', encoding='utf-8') as f:
        f.write(f"=== Video Folder: {video_folder} ===\n\n")

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

        with open(log_file, 'a', encoding='utf-8') as f:
            f.write(f"=== Video: {video_file.split('.')[0]} ===\n")
            for line in output_text:
                f.write(line + "\n")
            f.write('\n')