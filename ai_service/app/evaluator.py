import re
import json
from typing import Dict, Any
from .llm_client import query_llm

class SubmissionEvaluator:
    async def evaluate_essay(
        self, 
        question_text: str, 
        key_points: str, 
        student_answer: str,
        subject: str = "Công nghệ thông tin"
    ) -> Dict[str, Any]:
        if not student_answer or len(student_answer.strip()) < 5:
            return {
                "score": 0.0,
                "ai_feedback": "Thí sinh chưa nhập câu trả lời hoặc câu trả lời quá ngắn.",
                "analysis": "Không có nội dung để đánh giá."
            }

        # Token-optimized evaluation prompt with Rubric Breakdown
        system_prompt = (
            "Bạn là giám khảo chấm thi đại học ICTU. "
            "Chấm điểm bài làm sinh viên theo thang 10 và rubric: Ý chính (0-5đ), Lập luận logic (0-3đ), Ví dụ minh họa (0-2đ). "
            "Chỉ trả về JSON định dạng: {\"score\": 8.0, \"rubric\": {\"concepts\": 4.0, \"logic\": 2.5, \"examples\": 1.5}, \"ai_feedback\": \"nhận xét súc tích\"}"
        )

        user_prompt = (
            f"Môn: {subject}. "
            f"Câu hỏi: {question_text[:180]}. "
            f"Barem: {key_points[:150]}. "
            f"Bài làm SV: {student_answer[:400]}."
        )

        raw_res = await query_llm(user_prompt, system_prompt=system_prompt, max_tokens=220)
        if raw_res:
            try:
                clean_json = raw_res.strip()
                if "```json" in clean_json:
                    clean_json = clean_json.split("```json")[1].split("```")[0].strip()
                elif "```" in clean_json:
                    clean_json = clean_json.split("```")[1].split("```")[0].strip()
                
                data = json.loads(clean_json)
                if "score" in data and "ai_feedback" in data:
                    rubric = data.get("rubric", {"concepts": 4.0, "logic": 2.0, "examples": 1.5})
                    return {
                        "score": round(float(data["score"]), 1),
                        "rubric": rubric,
                        "ai_feedback": data["ai_feedback"],
                        "analysis": "Động cơ Khảo thí AI đánh giá tự động dựa trên chuẩn rubric và từ khóa trọng tâm."
                    }
            except Exception:
                pass

        # Fallback Heuristic
        return self._heuristic_evaluate(key_points, student_answer)

    def _heuristic_evaluate(self, key_points: str, student_answer: str) -> Dict[str, Any]:
        key_tokens = set(re.findall(r'\b\w{3,}\b', (key_points or "").lower()))
        student_tokens = set(re.findall(r'\b\w{3,}\b', student_answer.lower()))

        matched = key_tokens.intersection(student_tokens)
        match_ratio = len(matched) / (len(key_tokens) + 1e-5) if key_tokens else 0.5
        words = len(student_answer.split())
        length_factor = min(1.0, words / 50.0)

        raw_score = (match_ratio * 0.6 + length_factor * 0.4) * 10.0
        score = max(3.0, min(9.5, round(raw_score, 1))) if words > 10 else 2.0

        feedback = (
            "Bài làm đạt yêu cầu, đã đề cập đến các khái niệm trọng tâm."
            if score >= 6.5 else
            "Câu trả lời còn sơ lược, cần phân tích sâu hơn và bổ sung ví dụ minh họa."
        )

        return {
            "score": score,
            "ai_feedback": feedback,
            "analysis": f"Khớp từ khóa: {int(match_ratio*100)}% | Độ dài: {words} từ."
        }

submission_evaluator = SubmissionEvaluator()
