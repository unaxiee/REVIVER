from transformers import Qwen2_5_VLForConditionalGeneration, AutoTokenizer, AutoProcessor
from qwen_vl_utils import process_vision_info
import os
print('library imported')


model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
    "Qwen/Qwen2.5-VL-32B-Instruct",
    cache_dir='/storage/home/hcoda1/5/yxie405/scratch/hf_cache',
    torch_dtype="auto",
    device_map="auto"
).to("cuda")
print('model loaded')

processor = AutoProcessor.from_pretrained("Qwen/Qwen2.5-VL-32B-Instruct")
print('processor ready')

prompt_text_single_frame_dict = {
    "blue_square": "You're overlooking a T-shaped system, with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor) meeting at a central turntable. Describe where the blue item with a wooden pallet under it is in the image without mention the movement of the blue item.",
    "green_square": "You're overlooking a T-shaped system, with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor) meeting at a central turntable. Describe where the green item with a wooden pallet under it is in the image without mention the movement of the green item.",
    "pallet": "You're overlooking a T-shaped system, with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor) meeting at a central turntable. Describe where the wooden pallet is in the image without mention the movement of the pallet.",
    "white_box": "You're overlooking a T-shaped system, with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor) meeting at a central turntable. Describe where the white box with a wooden pallet under it is in the image without mention the movement of the box.",
    "yellow_box": "You're overlooking a T-shaped system, with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor) meeting at a central turntable. Describe where the yellow box with a wooden pallet under it is in the image without mention the movement of the box.",
}

prompt_text_multiple_frames_dict = {
    "blue_square": "You're overlooking a T-shaped system, with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor) meeting at a central turntable. Describe where the blue item with a wooden pallet under it is in each image. Don't assume the blue item and the pallet stay at the same place in all images.",
    "green_square": "You're overlooking a T-shaped system, with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor) meeting at a central turntable. Describe where the green item with a wooden pallet under it is in each image. Don't assume the green item and the pallet stay at the same place in all images",
    "pallet": "You're overlooking a T-shaped system, with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor) meeting at a central turntable. Describe where the wooden pallet is in each image. Don't assume the pallet stays at the same place in all images.",
    "white_box": "You're overlooking a T-shaped system, with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor) meeting at a central turntable. Describe where the white box with a wooden pallet under it is in each image. Don't assume the white box and the pallet stay at the same place in all images.",
    "yellow_box": "You're overlooking a T-shaped system, with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor) meeting at a central turntable. Describe where the yellow box with a wooden pallet under it is in each image. Don't assume the yellow box and the pallet stay at the same place in all images.",
}
print('prompt text ready')

log_file = 'output_query_frame.log'

for frame_folder in os.listdir('sampled_frames'):
    print(frame_folder)
    with open(log_file, 'a', encoding='utf-8') as f:
        f.write(f"=== Frame Folder: {frame_folder} ===\n\n")

    prompt_text = prompt_text_single_frame_dict[frame_folder]

    videos = set()
    for frame in os.listdir('sampled_frames/' + frame_folder):
        videos.add(frame.split('_frame_')[0])

    for video in videos:
        print(video)
        # Messages containing a local video path and a text query
        messages = [
            {
                "role": "user",
                "content": [
                    # {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_0.jpg"},
                    # {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_1.jpg"},
                    # {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_2.jpg"},
                    # {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_3.jpg"},
                    # {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_4.jpg"},
                    # {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_5.jpg"},
                    # {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_6.jpg"},
                    # {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_7.jpg"},
                    {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_8.jpg"},
                    # {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_9.jpg"},
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
            f.write(f"=== Video: {video} ===\n")
            for line in output_text:
                f.write(line + "\n")
            f.write('\n')