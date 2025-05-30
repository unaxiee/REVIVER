#!/bin/bash
#SBATCH -JQwenJob                             # Job name
#SBATCH --account=gts-szonouz6-paid             # Replace with your actual charge account
#SBATCH -N1
#SBATCH -t 03:00:00                                  # Time in minutes
#SBATCH -q inferno                             # QOS
#SBATCH --gpus=A100:1
#SBATCH -C A100-80GB
#SBATCH --mem-per-gpu=80G
#SBATCH -o qwen_output-%j.log                  # Output log file
#SBATCH --mail-type=BEGIN,END,FAIL
#SBATCH --mail-user=yxie405@gatech.edu

cd $SLURM_SUBMIT_DIR

module load anaconda3
conda activate qwen

srun python query_frame.py