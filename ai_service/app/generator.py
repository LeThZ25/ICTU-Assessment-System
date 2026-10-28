import re
import json
from typing import List, Dict, Any
from .rag_engine import doc_store
from .llm_client import query_llm

class QuestionGenerator:
    async def generate_questions(
        self, 
        doc_id: str, 
        subject: str, 
        num_mcq: int = 5, 
        num_essay: int = 1,
        difficulty: str = "Medium"
    ) -> List[Dict[str, Any]]:
        # Retrieve context from doc
        doc = doc_store.documents.get(doc_id)
        chunks = doc.get("chunks", []) if doc else []
        text_sample = ""
        if chunks:
            text_sample = " ".join([c["text"] for c in chunks[:4]])[:1200]

        # Bloom mapping
        bloom_mapping = {
            "Easy": ("Nhận biết & Thông hiểu", "CLO1"),
            "Medium": ("Vận dụng", "CLO2"),
            "Hard": ("Vận dụng cao & Phân tích", "CLO3")
        }
        bloom_level, default_clo = bloom_mapping.get(difficulty, ("Vận dụng", "CLO2"))

        system_prompt = (
            "Bạn là chuyên gia khảo thí đại học môn CNTT. "
            "Ra đề thi gắn liền với chuẩn đầu ra (CLO) và thang Bloom. "
            "CHỈ trả về JSON array hợp lệ, không thêm bất kỳ văn bản giải thích nào."
        )

        user_prompt = f"""
Môn: {subject}. Thang Bloom: {bloom_level}. Chuẩn đầu ra: {default_clo}.
Tài liệu tham khảo: "{text_sample}"

Hãy tạo đúng {num_mcq} câu trắc nghiệm (mcq) và {num_essay} câu tự luận (essay) theo mẫu JSON:
[
  {{
    "type": "mcq",
    "question": "Nội dung câu hỏi?",
    "option_a": "Lựa chọn A",
    "option_b": "Lựa chọn B",
    "option_c": "Lựa chọn C",
    "option_d": "Lựa chọn D",
    "correct_option": "A",
    "explanation": "Giải thích ngắn gọn lý do đúng",
    "difficulty": "{difficulty}",
    "bloom_level": "{bloom_level}",
    "clo_code": "{default_clo}",
    "source": "ai"
  }},
  {{
    "type": "essay",
    "question": "Nội dung câu hỏi tự luận?",
    "key_points": "Barem chấm điểm chi tiết",
    "difficulty": "{difficulty}",
    "bloom_level": "{bloom_level}",
    "clo_code": "{default_clo}",
    "source": "ai"
  }}
]
"""
        # Sufficient tokens budget to prevent truncation
        raw_response = await query_llm(user_prompt, system_prompt=system_prompt, max_tokens=2500)
        if raw_response:
            parsed = self._safe_parse_json(raw_response, difficulty, bloom_level, default_clo)
            if parsed and len(parsed) >= (num_mcq + num_essay):
                return self._normalize_questions(parsed[:num_mcq + num_essay])
            elif parsed and len(parsed) > 0:
                # Fill remaining with heuristic
                remaining_mcq = max(0, num_mcq - len([q for q in parsed if q.get("type") == "mcq"]))
                remaining_essay = max(0, num_essay - len([q for q in parsed if q.get("type") == "essay"]))
                fallback = self._generate_heuristic_questions(chunks, subject, remaining_mcq, remaining_essay, difficulty)
                return self._normalize_questions(parsed + fallback)

        # Fallback Heuristic Generator
        return self._normalize_questions(self._generate_heuristic_questions(chunks, subject, num_mcq, num_essay, difficulty))

    def _safe_parse_json(self, raw: str, difficulty: str, bloom_level: str, clo_code: str) -> List[Dict[str, Any]]:
        clean = raw.strip()
        if "```json" in clean:
            clean = clean.split("```json")[1].split("```")[0].strip()
        elif "```" in clean:
            clean = clean.split("```")[1].split("```")[0].strip()

        try:
            items = json.loads(clean)
            if isinstance(items, list):
                return items
        except Exception:
            pass

        # Try regex extraction of JSON objects
        extracted = []
        pattern = re.compile(r'\{[^{}]*"type"\s*:\s*"(?:mcq|essay)"[^{}]*\}', re.DOTALL)
        matches = pattern.findall(clean)
        for m in matches:
            try:
                obj = json.loads(m)
                extracted.append(obj)
            except Exception:
                continue

        return extracted

    def _normalize_questions(self, questions: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Normalize keys to support both camelCase and snake_case for C# and Frontend."""
        normalized = []
        for q in questions:
            q_type = q.get("type", "mcq")
            opt_a = q.get("option_a") or q.get("option_A") or q.get("OptionA") or ""
            opt_b = q.get("option_b") or q.get("option_B") or q.get("OptionB") or ""
            opt_c = q.get("option_c") or q.get("option_C") or q.get("OptionC") or ""
            opt_d = q.get("option_d") or q.get("option_D") or q.get("OptionD") or ""
            correct = q.get("correct_option") or q.get("correct_Option") or q.get("CorrectOption") or "A"

            item = {
                "type": q_type,
                "question": q.get("question") or q.get("QuestionText") or "",
                "option_a": opt_a,
                "option_b": opt_b,
                "option_c": opt_c,
                "option_d": opt_d,
                # camelCase keys for C# DTO compatibility
                "OptionA": opt_a,
                "OptionB": opt_b,
                "OptionC": opt_c,
                "OptionD": opt_d,
                "correct_option": correct.upper(),
                "CorrectOption": correct.upper(),
                "explanation": q.get("explanation") or q.get("Explanation") or "",
                "key_points": q.get("key_points") or q.get("KeyPoints") or "",
                "difficulty": q.get("difficulty") or "Medium",
                "bloom_level": q.get("bloom_level") or "Vận dụng",
                "clo_code": q.get("clo_code") or "CLO2",
                "source": q.get("source", "ai")
            }
            normalized.append(item)
        return normalized

    def _generate_heuristic_questions(
        self, 
        chunks: List[Dict], 
        subject: str, 
        num_mcq: int, 
        num_essay: int, 
        difficulty: str
    ) -> List[Dict[str, Any]]:
        results = []
        bank_mcq = [
            {
                "q": f"Trong môn {subject}, mục tiêu cốt lõi của việc chuẩn hóa quan hệ cơ sở dữ liệu là gì?",
                "a": "Giảm thiểu sự dư thừa dữ liệu và đảm bảo tính toàn vẹn thông tin",
                "b": "Tăng tốc độ hiển thị giao diện người dùng",
                "c": "Tự động sao lưu dữ liệu lên đám mây hàng ngày",
                "d": "Chuyển đổi dữ liệu có cấu trúc sang phi cấu trúc",
                "correct": "A",
                "exp": "Chuẩn hóa dữ liệu (1NF-3NF) loại bỏ các dị thường thêm, xóa, sửa.",
                "clo": "CLO1",
                "bloom": "Thông hiểu"
            },
            {
                "q": "Giao thức kiểm soát tương thời nào đảm bảo tính khả tuần tự (serializability) trong hệ quản trị CSDL?",
                "a": "Two-Phase Locking (2PL)",
                "b": "Round Robin Scheduling",
                "c": "First-In First-Out (FIFO)",
                "d": "Greedy Search",
                "correct": "A",
                "exp": "Giao thức khóa 2 pha (2PL) bảo đảm lịch trình các giao dịch là khả tuần tự.",
                "clo": "CLO2",
                "bloom": "Vận dụng"
            },
            {
                "q": "Cơ chế nào được sử dụng để chống gian lận kiểm tra thi cử trực tuyến cấp hệ điều hành?",
                "a": "Low-level Keyboard Hook chặn chuyển đổi cửa sổ và vô hiệu hóa Clipboard",
                "b": "Tắt nguồn máy tính khi phát hiện vi phạm",
                "c": "Ghi âm môi trường xung quanh liên tục",
                "d": "Tự động tắt card mạng của phòng học",
                "correct": "A",
                "exp": "Ứng dụng kiểm tra an toàn chặn can thiệp phím tắt hệ thống và ngăn cản copy/paste.",
                "clo": "CLO2",
                "bloom": "Vận dụng"
            },
            {
                "q": "Giao thức nào cho phép truyền dữ liệu hai chiều thời gian thực giữa Web Server và Client?",
                "a": "WebSocket / SignalR",
                "b": "HTTP 1.0",
                "c": "FTP",
                "d": "SMTP",
                "correct": "A",
                "exp": "SignalR và WebSocket hỗ trợ kết nối full-duplex thời gian thực độ trễ thấp.",
                "clo": "CLO1",
                "bloom": "Nhận biết"
            },
            {
                "q": "Phương pháp RAG (Retrieval-Augmented Generation) giải quyết nhược điểm nào của mô hình ngôn ngữ lớn?",
                "a": "Giảm thiểu hiện tượng ảo giác (hallucination) bằng cách truy xuất tài liệu kiểm chứng",
                "b": "Tăng kích thước mạng nơ-ron lên gấp đôi",
                "c": "Tự động sửa lỗi phần cứng máy chủ",
                "d": "Xóa bỏ bộ nhớ đệm RAM",
                "correct": "A",
                "exp": "RAG kết hợp truy xuất tri thức từ tài liệu riêng nạp vào ngữ cảnh suy luận.",
                "clo": "CLO3",
                "bloom": "Vận dụng cao"
            }
        ]

        idx = 0
        while len(results) < num_mcq:
            item = bank_mcq[idx % len(bank_mcq)]
            results.append({
                "type": "mcq",
                "question": item["q"],
                "option_a": item["a"],
                "option_b": item["b"],
                "option_c": item["c"],
                "option_d": item["d"],
                "correct_option": item["correct"],
                "explanation": item["exp"],
                "difficulty": difficulty,
                "bloom_level": item["bloom"],
                "clo_code": item["clo"],
                "source": "bank"
            })
            idx += 1

        for i in range(num_essay):
            results.append({
                "type": "essay",
                "question": f"Phân tích vai trò của Transaction và thuộc tính ACID trong các hệ quản trị cơ sở dữ liệu quan hệ môn {subject}.",
                "key_points": "Atomicity, Consistency, Isolation, Durability; đảm bảo tính toàn vẹn dữ liệu khi có sự cố hệ thống.",
                "difficulty": difficulty,
                "bloom_level": "Vận dụng cao & Phân tích",
                "clo_code": "CLO3",
                "source": "bank"
            })

        return results

question_generator = QuestionGenerator()
