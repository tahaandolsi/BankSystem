-- Apply manually after authorization.
ALTER TABLE public."Executions"
ADD COLUMN IF NOT EXISTS "ContentJson" text NULL;
