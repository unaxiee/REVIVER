#!/bin/bash
#SBATCH -JQwenJob                             # Job name
#SBATCH --account=gts-szonouz6             # Replace with your actual charge account
#SBATCH -N1
#SBATCH -t 1:00:00                                  # Time in minutes
#SBATCH -q inferno                             # QOS
#SBATCH --gpus=H200:1
#SBATCH --mem-per-gpu=140G
#SBATCH -o qwen_output-%j.log                  # Output log file
#SBATCH --mail-type=BEGIN,END,FAIL
#SBATCH --mail-user=yxie405@gatech.edu

cd $SLURM_SUBMIT_DIR

module load anaconda3
source /storage/home/hcoda1/5/yxie405/p-szonouz6-0/.venv/bin/activate

export HF_HOME=/storage/home/hcoda1/5/yxie405/p-szonouz6-0/hf_cache
export TRANSFORMERS_CACHE=/storage/home/hcoda1/5/yxie405/p-szonouz6-0/hf_cache
export HF_HUB_CACHE=/storage/home/hcoda1/5/yxie405/p-szonouz6-0/hf_cache
export TMPDIR=/storage/home/hcoda1/5/yxie405/p-szonouz6-0/tmp

srun python query_video.py