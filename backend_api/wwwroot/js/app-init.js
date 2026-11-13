// ==============================================================
// 12. APPLICATION INITIALIZATION & DOM READY HOOKS
// ==============================================================
window.addEventListener('DOMContentLoaded', () => {
  checkAuthentication();
  const savedTheme = sessionStorage.getItem('ictu_theme') || 'ictu';
  applyTheme(savedTheme);
  loadNotifications();
  initSignalR();
  loadClasses();
  loadSchedule();
  loadGroupedQuestionBank();
  loadExams();
  setTimeout(initStudioQuestionsIfEmpty, 300);
});
