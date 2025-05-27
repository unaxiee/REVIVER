from transformers import Qwen2_5_VLForConditionalGeneration, AutoTokenizer, AutoProcessor
from qwen_vl_utils import process_vision_info
import os


model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
    "Qwen/Qwen2.5-VL-32B-Instruct",
    torch_dtype="auto",
    device_map="auto"
).to("cuda")

processor = AutoProcessor.from_pretrained("Qwen/Qwen2.5-VL-32B-Instruct")

video_HighBox = ['HighBox/' + video for video in os.listdir('HighBox')]
video_LowBox = ['LowBox/' + video for video in os.listdir('LowBox')]

text_HighBox = "You are observing a T-shape conveyor-based sorting system in a factory setting. The system has the following components: 1. A horizontal conveyor on the right side, where the process begins. 2. A turntable located at the center left. 3. A vertical conveyor on the far left, split into two sections: one above and one below the turntable. The intended movement of the box is as follows: 1. The box moves from right to left along the horizontal conveyor. 2. The box is loaded onto the turntable. 3. The turntable rotates and unloads the box onto the up part of the vertical conveyor. 4. The box moves upwards along the vertical conveyor. 5. The box should reach the final destination at the top end of the vertical conveyor. Task: Carefully track the box throughout the video. Determine whether it reaches the correct final destination. If it does not complete the process successfully, identify at which step the failure occurred and what went wrong.Think step by step while analyzing the sequence. Base your reasoning only on the actual behavior of the box in the video."
text_LowBox = "You are observing a T-shape conveyor-based sorting system in a factory setting. The system has the following components: 1. A horizontal conveyor on the right side, where the process begins. 2. A turntable located at the center left. 3. A vertical conveyor on the far left, split into two sections: one above and one below the turntable. The intended movement of the box is as follows: 1. The box moves from right to left along the horizontal conveyor. 2. The box is loaded onto the turntable. 3. The turntable rotates and unloads the box onto the down part of the vertical conveyor. 4. The box moves downwards along the vertical conveyor. 5. The box should reach the final destination at the bottom end of the vertical conveyor. Task: Carefully track the box throughout the video. Determine whether it reaches the correct final destination. If it does not complete the process successfully, identify at which step the failure occurred and what went wrong.Think step by step while analyzing the sequence. Base your reasoning only on the actual behavior of the box in the video."


def process_videos(videos, text_instruction):
    for video in videos:
        # Messages containing a local video path and a text query
        messages = [
            {
                "role": "user",
                "content": [
                    {
                        "type": "video",
                        "video": video,
                        "max_pixels": 480 * 270,
                        "fps": 1,
                    },
                    {"type": "text", "text": text_instruction}
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

        log_file = 'output' + video.split('/')[1].split('.')[0] + '.log'
        with open(log_file, 'a', encoding='utf-8') as f:
            for line in output_text:
                f.write(line + "\n\n")
        break


process_videos(video_HighBox, text_HighBox)
# process_videos(video_LowBox, text_LowBox)