from typing import Dict, List, Any
from .llm_client import query_llm

class SocraticOralAgent:
    def __init__(self):
        # Memory storage: session_key -> List[{"role": str, "content": str}]
        self.sessions: Dict[str, List[Dict[str, str]]] = {}

    def get_history(self, student_id: str, exam_id: int) -> List[Dict[str, str]]:
        key = f"{student_id}_{exam_id}"
        if key not in self.sessions:
            self.sessions[key] = []
        return self.sessions[key]

    def add_message(self, student_id: str, exam_id: int, role: str, content: str):
        key = f"{student_id}_{exam_id}"
        if key not in self.sessions:
            self.sessions[key] = []
        self.sessions[key].append({"role": role, "content": content})
        # Token optimization: keep only last 6 turns
        if len(self.sessions[key]) > 6:
            self.sessions[key] = self.sessions[key][-6:]

    async def interact(
        self, 
        student_id: str, 
        exam_id: int, 
        user_message: str, 
        current_question: str = "",
        context: str = ""
    ) -> str:
        history = self.get_history(student_id, exam_id)
        self.add_message(student_id, exam_id, "student", user_message)

        # Token-optimized concise system prompt
        system_prompt = (
            "Bạn là AI Giám khảo khảo thí ĐH CNTT & Truyền Thông (ICTU). "
            "Giao tiếp bằng tiếng Việt, ân cần nhưng chuẩn mực học thuật. "
            "Dùng phương pháp gợi mở (Socratic) để sinh viên tự tìm ra đáp án, KHÔNG chép lời giải hoàn chỉnh. "
            "Giữ câu trả lời ngắn gọn, súc tích (tối đa 3 câu)."
        )

        history_summary = " ".join([f"{m['role']}: {m['content']}" for m in history[-4:]])
        user_prompt = (
            f"Môn học: {context}. "
            f"Câu hỏi sinh viên đang làm: \"{current_question[:150]}\". "
            f"Lịch sử trao đổi gần nhất: {history_summary}. "
            f"Tin nhắn mới của sinh viên: \"{user_message}\". "
            "Hãy phản hồi và đặt 1 câu hỏi định hướng ngắn gọn:"
        )

        llm_reply = await query_llm(user_prompt, system_prompt=system_prompt, max_tokens=180)
        if llm_reply and len(llm_reply.strip()) > 8:
            reply = llm_reply.strip()
            self.add_message(student_id, exam_id, "ai", reply)
            return reply

        # Fallback if API offline
        reply = self._generate_adaptive_reply(user_message, current_question, history)
        self.add_message(student_id, exam_id, "ai", reply)
        return reply

    def _generate_adaptive_reply(self, message: str, question: str, history: List[Dict[str, str]]) -> str:
        msg = message.strip().lower()
        q_lower = (question or "").lower()

        if msg in ["hả?", "hả", "sao cơ?", "gì cơ?", "?", "???"]:
            if "acid" in q_lower or "transaction" in q_lower:
                return "Đừng lo lắng! Hãy nghĩ đến việc chuyển tiền qua app ngân hàng: tiền trừ bên này thì phải cộng bên kia. Nếu lỗi mạng thì phải hủy (Rollback). Đó chính là Tính nguyên tử (Atomicity). Bạn thấy tính chất này giải quyết điều gì?"
            return "Tôi đang hướng dẫn bạn. Hãy đọc lại đề bài và tìm từ khóa quen thuộc nhất với bạn xem nào?"

        if any(w in msg for w in ["khó", "không biết", "không hiểu", "bí", "chịu"]):
            if "acid" in q_lower or "transaction" in q_lower:
                return "Câu hỏi về ACID rất hay gặp trong thực tế. Hãy nhớ lại 4 chữ cái: A (Nguyên tử), C (Nhất quán), I (Cô lập), D (Bền vững). Bạn hãy thử giải thích chữ cái đầu tiên nhé."
            return f"Hãy chia câu hỏi làm 2 ý: Khái niệm là gì và ứng dụng ở đâu. Bạn gõ thử ý đầu tiên xem nào?"

        return f"AI Giám khảo đã ghi nhận ý kiến. Hãy tập trung vào bản chất kỹ thuật của câu hỏi '{question[:50]}...' để làm bài nhé."

socratic_agent = SocraticOralAgent()
