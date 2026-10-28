import os
import shutil
import uuid
from typing import List, Optional
from fastapi import FastAPI, UploadFile, File, Form, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import StreamingResponse
from pydantic import BaseModel

from .rag_engine import doc_store
from .generator import question_generator
from .evaluator import submission_evaluator
from .oral_chat import socratic_agent
from .llm_client import stream_llm, query_llm, check_ollama_available

app = FastAPI(
    title="ICTU AI Assessment Agent Service",
    description="Module 3: AI Agent for Document Ingestion, RAG, Exam Generation & Grading",
    version="1.0.0"
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

TEMP_UPLOAD_DIR = os.path.join(os.path.dirname(__file__), "..", "uploads")
os.makedirs(TEMP_UPLOAD_DIR, exist_ok=True)

class GenerateExamRequest(BaseModel):
    subject: str
    document_id: Optional[str] = None
    num_mcq: int = 5
    num_essay: int = 2
    difficulty: str = "Medium"

class GradeRequest(BaseModel):
    question_text: str
    key_points: str
    student_answer: str
    subject: Optional[str] = "Công nghệ thông tin"

class OralChatRequest(BaseModel):
    student_id: str
    exam_id: int
    user_message: str
    current_question: Optional[str] = ""
    context: Optional[str] = ""

@app.get("/")
def root():
    return {
        "service": "ICTU AI Assessment Agent",
        "status": "Online",
        "version": "1.0.0",
        "docs_count": len(doc_store.documents)
    }

@app.get("/health")
async def health():
    ollama_ok = await check_ollama_available()
    return {
        "status": "ok",
        "ollama_connected": ollama_ok,
        "indexed_documents": len(doc_store.documents)
    }

@app.post("/api/ai/upload-document")
async def upload_document(file: UploadFile = File(...)):
    try:
        doc_id = str(uuid.uuid4())[:8]
        filename = file.filename or f"doc_{doc_id}.pdf"
        file_path = os.path.join(TEMP_UPLOAD_DIR, f"{doc_id}_{filename}")
        
        with open(file_path, "wb") as buffer:
            shutil.copyfileobj(file.file, buffer)

        # Index in RAG engine
        doc_info = doc_store.add_document(doc_id, filename, file_path)

        return {
            "success": True,
            "document_id": doc_id,
            "filename": filename,
            "chunks_count": doc_info["num_chunks"],
            "word_count": doc_info["word_count"],
            "preview": doc_info["text"][:300] + "..." if len(doc_info["text"]) > 300 else doc_info["text"]
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Lỗi tải tài liệu: {str(e)}")

@app.get("/api/ai/documents")
def list_documents():
    return [
        {
            "doc_id": doc["doc_id"],
            "filename": doc["filename"],
            "num_chunks": doc["num_chunks"],
            "word_count": doc["word_count"]
        }
        for doc in doc_store.documents.values()
    ]

@app.post("/api/ai/generate-questions")
async def generate_questions(req: GenerateExamRequest):
    try:
        questions = await question_generator.generate_questions(
            doc_id=req.document_id,
            subject=req.subject,
            num_mcq=req.num_mcq,
            num_essay=req.num_essay,
            difficulty=req.difficulty
        )
        return {
            "success": True,
            "subject": req.subject,
            "total_questions": len(questions),
            "questions": questions
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Lỗi sinh câu hỏi: {str(e)}")

@app.post("/api/ai/grade-submission")
async def grade_submission(req: GradeRequest):
    try:
        result = await submission_evaluator.evaluate_essay(
            question_text=req.question_text,
            key_points=req.key_points,
            student_answer=req.student_answer,
            subject=req.subject or "Tin học"
        )
        return {
            "success": True,
            "score": result["score"],
            "ai_feedback": result["ai_feedback"],
            "analysis": result["analysis"]
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Lỗi chấm bài AI: {str(e)}")

@app.post("/api/ai/chat-oral-exam")
async def chat_oral_exam(req: OralChatRequest):
    reply_text = await socratic_agent.interact(
        student_id=req.student_id,
        exam_id=req.exam_id,
        user_message=req.user_message,
        current_question=req.current_question or "",
        context=req.context or "Hệ thống CNTT & Cơ sở dữ liệu"
    )
    return {
        "success": True,
        "reply": reply_text
    }

@app.get("/api/ai/chat-oral-stream")
async def chat_oral_stream(message: str, student_id: str = "SV01"):
    prompt = f"Bạn là AI Giám khảo ICTU hỏi thi vấn đáp sinh viên {student_id}. Phản hồi câu trả lời: '{message}'."
    
    async def event_generator():
        async for chunk in stream_llm(prompt):
            yield f"data: {chunk}\n\n"
        yield "data: [DONE]\n\n"

    return StreamingResponse(event_generator(), media_type="text/event-stream")

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("app.main:app", host="0.0.0.0", port=8000, reload=True)

