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

base_prompt = (
    "You are given three reference images of a {complete_item}, each showing its location on a T-shaped conveyor system. "
    "This system includes a long horizontal conveyor, a short top vertical conveyor, and a short bottom vertical conveyor, which all connect at a central turntable. "
    "Each reference image is accompanied by a short description that explains where the {item} is located. "
    "Use the visual and textual information from these reference images to understand how to recognize the {item}'s location. "
    "After the reference images, you will be shown several query images. "
    "For each query image, describe where the {complete_item} is located within the conveyor system. "
    "The {item}'s location may differ between query images. "
    "Do not repeat the content of the reference images, but use them to guide your reasoning."
)
prompt_text_dict = {
    "blue_square": base_prompt.format(complete_item="blue item with a wooden pallet under it", item="blue item"),
    "green_square": base_prompt.format(complete_item="green item with a wooden pallet under it", item="green item"),
    "pallet": base_prompt.format(complete_item="wooden pallet", item="pallet"),
    "white_box": base_prompt.format(complete_item="white box with a wooden pallet under it", item="white box"),
    "yellow_box": base_prompt.format(complete_item="yellow box with a wooden pallet under it", item="yellow box")
}
print('prompt text ready')

base_path = "reference_frames/{item}/{item}_{location_key}.jpg"
base_description = "This image depicts the {item} on the {location_description}."
item_dict = {
    "blue_square": ("blue item with a wooden pallet under it", "short bottom vertical conveyor"),
    "green_square": ("green item with a wooden pallet under it", "short bottom vertical conveyor"),
    "pallet": ("wooden pallet", "short bottom vertical conveyor"),
    "white_box": ("white box with a wooden pallet under it", "short top vertical conveyor"),
    "yellow_box": ("yellow box with a wooden pallet under it", "short bottom vertical conveyor")
}
ref_frame_dict = {}
for item, item_info in item_dict.items():
    ref_frame_dict[item] = [
        (base_path.format(item=item, location_key="horizontal"), base_description.format(item=item_info[0], location_description="long horizontal conveyor")),
        (base_path.format(item=item, location_key="turntable"), base_description.format(item=item_info[0], location_description="turntable")),
        (base_path.format(item=item, location_key="vertical"), base_description.format(item=item_info[0], location_description=item_info[1]))
    ]

log_file = 'output_query_frame_context.log'

user_define_frames = {
    1: [8],
    2: [0, 8],
    3: [0, 5, 8]
}

with open(log_file, 'a', encoding='utf-8') as f:
    for frame_folder in os.listdir("sampled_frames"):
        print(frame_folder)
        f.write(f"=== Item: {frame_folder} ===\n\n")

        videos = set()
        for frame in os.listdir('sampled_frames/' + frame_folder):
            videos.add(frame.split('_frame_')[0])

        ref_frame = ref_frame_dict[frame_folder]

        for video in videos:
            print(video)
            f.write(f"=== Manipulation: {video} ===\n\n")

            for num_frame in range(1, 11):
                if num_frame <= 3:
                    query_frames = user_define_frames[num_frame]
                else:
                    query_frames = np.linspace(0, 9, num_frame, dtype=int).tolist()

                prompt_text = prompt_text_dict[frame_folder]

                # Messages containing a local video path and a text query
                messages = [
                    {
                        "role": "user",
                        "content": [
                            *[
                                item
                                for idx_r, (path, description) in enumerate(ref_frame)
                                for item in [
                                    {"type": "text", "text": f"Reference Image {idx_r+1}: {description}"},
                                    {"type": "image", "image": path}
                                ]
                            ],
                            *[
                                item 
                                for idx_q, idx_f in enumerate(query_frames)
                                for item in [
                                    {"type": "text", "text": f"Query Image {idx_q+1}"},
                                    {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_{idx_f}.jpg"}
                                ]
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
                generated_ids = model.generate(**inputs, max_new_tokens=512)
                generated_ids_trimmed = [
                    out_ids[len(in_ids) :] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)
                ]
                output_text = processor.batch_decode(
                    generated_ids_trimmed, skip_special_tokens=True, clean_up_tokenization_spaces=False
                )
                
                f.write(f"=== Number of frames: {num_frame} ===\n")
                for line in output_text:
                    f.write(line + "\n")
                f.write('\n')